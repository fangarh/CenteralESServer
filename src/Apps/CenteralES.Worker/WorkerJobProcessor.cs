using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using CenteralES.Storage;

namespace CenteralES.Worker;

public sealed class WorkerJobProcessor
{
    private readonly ILogger<WorkerJobProcessor> _logger;
    private readonly IProcessingJobQueue _queue;
    private readonly IPdfStampRecognizer _recognizer;
    private readonly IPdfStampRecognitionResultStore _resultStore;
    private readonly ITemporaryFileStore _temporaryFileStore;

    public WorkerJobProcessor(
        ILogger<WorkerJobProcessor> logger,
        IProcessingJobQueue queue,
        IPdfStampRecognizer recognizer,
        IPdfStampRecognitionResultStore resultStore,
        ITemporaryFileStore temporaryFileStore)
    {
        _logger = logger;
        _queue = queue;
        _recognizer = recognizer;
        _resultStore = resultStore;
        _temporaryFileStore = temporaryFileStore;
    }

    public async Task ProcessAsync(ClaimedProcessingJob job, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing job {JobId} attempt {AttemptNumber}.", job.JobId, job.AttemptNumber);

            await using var pdfContent = await _temporaryFileStore.OpenReadAsync(job.TemporaryFileKey, cancellationToken);
            var adapterResult = await _recognizer.RecognizeAsync(job, pdfContent, cancellationToken);
            var saved = await _resultStore.SaveAsync(
                new SavePdfStampRecognitionResultCommand(
                    job.SubjectId,
                    job.JobId,
                    job.ContentHash,
                    adapterResult.PayloadJson,
                    adapterResult.ContractVersion,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            await _queue.CompleteAsync(
                new CompleteProcessingJobCommand(job.JobId, job.SubjectId, saved.ResultIndexId, adapterResult.Diagnostics, DateTimeOffset.UtcNow),
                cancellationToken);
            await DeleteTemporaryFileAfterCompletionAsync(job, cancellationToken);

            _logger.LogInformation("Completed job {JobId}.", job.JobId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Job {JobId} failed in worker.", job.JobId);

            await _queue.FailAsync(
                new FailProcessingJobCommand(
                    job.JobId,
                    job.SubjectId,
                    NormalizedProcessorError.InternalError,
                    Final: false,
                    new AttemptDiagnostics(
                        Endpoint: "worker://internal",
                        Duration: null,
                        HttpStatus: null,
                        NormalizedError: NormalizedProcessorError.InternalError,
                        Retryable: false,
                        CorrelationId: Guid.NewGuid().ToString("N"),
                        RawErrorExcerpt: ex.Message),
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private async Task DeleteTemporaryFileAfterCompletionAsync(ClaimedProcessingJob job, CancellationToken cancellationToken)
    {
        try
        {
            await _temporaryFileStore.DeleteIfExistsAsync(job.TemporaryFileKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Temporary file cleanup failed for completed job {JobId}.", job.JobId);
        }
    }
}
