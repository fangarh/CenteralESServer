using CenteralES.Processing.Queue;
using CenteralES.Storage;

namespace CenteralES.PdfStampRecognition;

public sealed record SubmitPdfStampRecognitionJobCommand(
    Func<Stream> OpenReadStream,
    long FileLength,
    ContentHashAlgorithm HashAlgorithm,
    DateTimeOffset SubmittedAt);

public abstract record SubmitPdfStampRecognitionJobResult;

public sealed record SubmitPdfStampRecognitionJobCompleted(PdfStampRecognitionResult Result)
    : SubmitPdfStampRecognitionJobResult;

public sealed record SubmitPdfStampRecognitionJobAccepted(
    string ContentHash,
    Guid JobId,
    int AttemptNumber,
    CenteralES.Processing.ProcessingJobStatus Status,
    bool Deduplicated)
    : SubmitPdfStampRecognitionJobResult;

public sealed record SubmitPdfStampRecognitionJobTemporaryStorageFull()
    : SubmitPdfStampRecognitionJobResult;

public sealed class SubmitPdfStampRecognitionJobHandler
{
    private readonly IProcessingJobCommandQueue _commandQueue;
    private readonly IProcessingJobReadStore _jobReadStore;
    private readonly IPdfStampRecognitionResultStore _resultStore;
    private readonly IContentHasher _contentHasher;
    private readonly ITemporaryFileStore _temporaryFileStore;
    private readonly ITemporaryStorageMonitor _temporaryStorageMonitor;

    public SubmitPdfStampRecognitionJobHandler(
        IProcessingJobCommandQueue commandQueue,
        IProcessingJobReadStore jobReadStore,
        IPdfStampRecognitionResultStore resultStore,
        IContentHasher contentHasher,
        ITemporaryFileStore temporaryFileStore,
        ITemporaryStorageMonitor temporaryStorageMonitor)
    {
        _commandQueue = commandQueue;
        _jobReadStore = jobReadStore;
        _resultStore = resultStore;
        _contentHasher = contentHasher;
        _temporaryFileStore = temporaryFileStore;
        _temporaryStorageMonitor = temporaryStorageMonitor;
    }

    public async Task<SubmitPdfStampRecognitionJobResult> HandleAsync(
        SubmitPdfStampRecognitionJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var hashStream = command.OpenReadStream();
        var hashes = await _contentHasher.ComputeAllAsync(hashStream, cancellationToken);
        var canonicalHash = hashes.Single(hash => hash.Algorithm == command.HashAlgorithm);
        var hash = canonicalHash.Value;
        var processingHashes = hashes
            .Select(item => new ProcessingContentHash(item.AlgorithmName, item.Value))
            .ToArray();

        var existingResult = await FindExistingResultAsync(hashes, cancellationToken);
        if (existingResult is not null)
        {
            await RegisterContentHashesAsync(existingResult.SubjectId, command.SubmittedAt, processingHashes, cancellationToken);
            return new SubmitPdfStampRecognitionJobCompleted(existingResult);
        }

        var currentJob = await FindCurrentJobAsync(hashes, cancellationToken);
        if (currentJob is not null && currentJob.Status is Processing.ProcessingJobStatus.Queued or Processing.ProcessingJobStatus.Processing)
        {
            await RegisterContentHashesAsync(currentJob.SubjectId, command.SubmittedAt, processingHashes, cancellationToken);
            return new SubmitPdfStampRecognitionJobAccepted(
                hash,
                currentJob.JobId,
                currentJob.AttemptNumber,
                currentJob.Status,
                Deduplicated: true);
        }

        var capacity = await _temporaryStorageMonitor.CheckCapacityAsync(
            new TemporaryStorageCapacityRequest(command.FileLength),
            cancellationToken);
        if (capacity.Status is TemporaryStorageCapacityStatus.Full)
        {
            return new SubmitPdfStampRecognitionJobTemporaryStorageFull();
        }

        var temporaryFileKey = $"incoming/{ContentHash.ToTemporaryStorageKeySegment(hash)}.pdf";
        await using (var saveStream = command.OpenReadStream())
        {
            await _temporaryFileStore.SaveAsync(temporaryFileKey, saveStream, cancellationToken);
        }

        var enqueueResult = await _commandQueue.EnqueueAsync(
            new CreateProcessingJobCommand(
                PdfStampRecognitionConstants.Capability,
                hash,
                temporaryFileKey,
                command.SubmittedAt,
                processingHashes),
            cancellationToken);

        return new SubmitPdfStampRecognitionJobAccepted(
            hash,
            enqueueResult.JobId,
            enqueueResult.AttemptNumber,
            enqueueResult.Status,
            enqueueResult.Deduplicated);
    }

    private async Task<PdfStampRecognitionResult?> FindExistingResultAsync(
        IReadOnlyList<ContentHashValue> hashes,
        CancellationToken cancellationToken)
    {
        foreach (var hash in hashes.Select(item => item.Value).Distinct(StringComparer.Ordinal))
        {
            var result = await _resultStore.GetByHashAsync(hash, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private async Task<ProcessingJobSnapshot?> FindCurrentJobAsync(
        IReadOnlyList<ContentHashValue> hashes,
        CancellationToken cancellationToken)
    {
        foreach (var hash in hashes.Select(item => item.Value).Distinct(StringComparer.Ordinal))
        {
            var currentJob = await _jobReadStore.GetCurrentByHashAsync(
                PdfStampRecognitionConstants.Capability,
                hash,
                cancellationToken);
            if (currentJob is not null)
            {
                return currentJob;
            }
        }

        return null;
    }

    private Task RegisterContentHashesAsync(
        Guid subjectId,
        DateTimeOffset registeredAt,
        IReadOnlyList<ProcessingContentHash> hashes,
        CancellationToken cancellationToken)
    {
        return _commandQueue.RegisterContentHashesAsync(
            new RegisterProcessingContentHashesCommand(
                subjectId,
                PdfStampRecognitionConstants.Capability,
                registeredAt,
                hashes),
            cancellationToken);
    }
}
