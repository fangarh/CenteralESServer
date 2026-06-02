using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using CenteralES.Storage;

namespace CenteralES.Worker;

public sealed class WorkerJobProcessor
{
    private readonly ILogger<WorkerJobProcessor> _logger;
    private readonly WorkerJobProcessorOptions _options;
    private readonly IProcessingJobQueue _queue;
    private readonly IPdfStampRecognizer _recognizer;
    private readonly IPdfStampRecognitionResultStore _resultStore;
    private readonly ITemporaryFileStore _temporaryFileStore;

    public WorkerJobProcessor(
        ILogger<WorkerJobProcessor> logger,
        IProcessingJobQueue queue,
        IPdfStampRecognizer recognizer,
        IPdfStampRecognitionResultStore resultStore,
        ITemporaryFileStore temporaryFileStore,
        WorkerJobProcessorOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new WorkerJobProcessorOptions();
        _options.Validate();
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
            if (ProcessingInputRetentionPolicy.ShouldDeleteTemporaryInputAfterTerminalState(ProcessingJobStatus.Completed))
            {
                await DeleteTemporaryFileAfterTerminalStateAsync(job, cancellationToken);
            }

            _logger.LogInformation("Completed job {JobId}.", job.JobId);
        }
        catch (PdfStampRecognitionAdapterException ex)
        {
            if (ex.Error is NormalizedProcessorError.ProcessorOverloaded)
            {
                var now = DateTimeOffset.UtcNow;
                _logger.LogInformation("Job {JobId} processor pool is overloaded. Deferring job until {ScheduledAt}.", job.JobId, now.Add(_options.ProcessorOverloadedDelay));

                await _queue.DeferAsync(
                    new DeferProcessingJobCommand(
                        job.JobId,
                        job.SubjectId,
                        now.Add(_options.ProcessorOverloadedDelay),
                        now),
                    cancellationToken);
                return;
            }

            _logger.LogWarning(ex, "Job {JobId} failed in PDF stamp recognizer with normalized error {Error}.", job.JobId, ex.Error);
            var classification = ProcessorErrorClassifier.Classify(ex.Error);
            var final = !classification.IsRetryable || job.AttemptNumber >= _options.MaxAttempts;

            await _queue.FailAsync(
                new FailProcessingJobCommand(
                    job.JobId,
                    job.SubjectId,
                    ex.Error,
                    Final: final,
                    ex.Diagnostics,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            LogPreservedTemporaryFileForManualRetry(final, job);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Job {JobId} failed in worker.", job.JobId);
            var classification = ProcessorErrorClassifier.Classify(NormalizedProcessorError.InternalError);
            var final = !classification.IsRetryable || job.AttemptNumber >= _options.MaxAttempts;

            await _queue.FailAsync(
                new FailProcessingJobCommand(
                    job.JobId,
                    job.SubjectId,
                    NormalizedProcessorError.InternalError,
                    Final: final,
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

            LogPreservedTemporaryFileForManualRetry(final, job);
        }
    }

    private async Task DeleteTemporaryFileAfterTerminalStateAsync(ClaimedProcessingJob job, CancellationToken cancellationToken)
    {
        try
        {
            await _temporaryFileStore.DeleteIfExistsAsync(job.TemporaryFileKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Temporary file cleanup failed for terminal job {JobId}.", job.JobId);
        }
    }

    private void LogPreservedTemporaryFileForManualRetry(bool final, ClaimedProcessingJob job)
    {
        if (final)
        {
            _logger.LogInformation(
                "Preserving temporary file {TemporaryFileKey} for final failed job {JobId} so manual retry can reuse the original input.",
                job.TemporaryFileKey,
                job.JobId);
        }
    }
}

public sealed class WorkerJobProcessorOptions
{
    public int MaxAttempts { get; init; } = 5;
    public TimeSpan ProcessorOverloadedDelay { get; init; } = TimeSpan.FromSeconds(15);

    public void Validate()
    {
        if (MaxAttempts <= 0)
        {
            throw new InvalidOperationException("Worker job processor maxAttempts must be greater than zero.");
        }

        if (ProcessorOverloadedDelay <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker job processor processorOverloadedDelay must be greater than zero.");
        }
    }
}
