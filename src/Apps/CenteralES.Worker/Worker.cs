using CenteralES.Processing.Queue;
using CenteralES.Processing.Workers;
using CenteralES.PdfStampRecognition;

namespace CenteralES.Worker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JobHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private readonly ILogger<Worker> _logger;
    private readonly IProcessingJobQueue _queue;
    private readonly IWorkerHeartbeatStore _heartbeatStore;
    private readonly WorkerJobProcessor _processor;
    private readonly string _workerId;
    private readonly DateTimeOffset _startedAt;

    public Worker(
        ILogger<Worker> logger,
        IProcessingJobQueue queue,
        IWorkerHeartbeatStore heartbeatStore,
        WorkerJobProcessor processor)
    {
        _logger = logger;
        _queue = queue;
        _heartbeatStore = heartbeatStore;
        _processor = processor;
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        _startedAt = DateTimeOffset.UtcNow;
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
                await _heartbeatStore.HeartbeatAsync(
                    new HeartbeatWorkerCommand(
                        _workerId,
                        PdfStampRecognitionConstants.ProcessorKey,
                        PdfStampRecognitionConstants.Capability,
                        _startedAt,
                        now),
                    stoppingToken);
                _logger.LogInformation("Worker {WorkerId} heartbeat at {HeartbeatAt}.", _workerId, now);
                nextHeartbeatAt = now.Add(HeartbeatInterval);
            }

            var job = await _queue.ClaimNextAsync(now, stoppingToken);
            if (job is null)
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            await ProcessWithJobHeartbeatAsync(job, stoppingToken);
        }
    }

    private async Task ProcessWithJobHeartbeatAsync(ClaimedProcessingJob job, CancellationToken stoppingToken)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = RefreshJobHeartbeatUntilStoppedAsync(job.JobId, heartbeatCts.Token);

        try
        {
            await _processor.ProcessAsync(job, stoppingToken);
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException) when (heartbeatCts.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RefreshJobHeartbeatUntilStoppedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(JobHeartbeatInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            await _queue.RefreshHeartbeatAsync(
                new RefreshProcessingJobHeartbeatCommand(jobId, now),
                cancellationToken);
        }
    }
}
