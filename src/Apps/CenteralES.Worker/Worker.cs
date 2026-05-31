using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;

namespace CenteralES.Worker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private readonly ILogger<Worker> _logger;
    private readonly IProcessingJobQueue _queue;
    private readonly IPdfStampRecognizer _recognizer;
    private readonly IPdfStampRecognitionResultStore _resultStore;

    public Worker(
        ILogger<Worker> logger,
        IProcessingJobQueue queue,
        IPdfStampRecognizer recognizer,
        IPdfStampRecognitionResultStore resultStore)
    {
        _logger = logger;
        _queue = queue;
        _recognizer = recognizer;
        _resultStore = resultStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CenteralES worker started.");

        var nextHeartbeatAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextHeartbeatAt)
            {
                _logger.LogInformation("Worker heartbeat at {HeartbeatAt}.", now);
                nextHeartbeatAt = now.Add(HeartbeatInterval);
            }

            var job = await _queue.ClaimNextAsync(now, stoppingToken);
            if (job is null)
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            await ProcessJobAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(ClaimedProcessingJob job, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing job {JobId} attempt {AttemptNumber}.", job.JobId, job.AttemptNumber);

            var adapterResult = await _recognizer.RecognizeAsync(job, cancellationToken);
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
}
