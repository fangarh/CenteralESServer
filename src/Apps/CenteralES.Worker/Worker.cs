using CenteralES.Processing.Queue;
using CenteralES.Processing.Workers;
using CenteralES.PdfStampRecognition;

namespace CenteralES.Worker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan EndpointConfigurationRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JobHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private readonly ILogger<Worker> _logger;
    private readonly IProcessingJobClaimQueue _queue;
    private readonly IProcessingJobRecoveryQueue _recoveryQueue;
    private readonly IWorkerHeartbeatStore _heartbeatStore;
    private readonly IReadOnlyList<IWorkerEndpointMetricsProvider> _endpointMetricsProviders;
    private readonly IReadOnlyList<IWorkerEndpointConfigurationRefresher> _endpointConfigurationRefreshers;
    private readonly WorkerJobProcessor _processor;
    private readonly WorkerRecoveryOptions _recoveryOptions;
    private readonly string _workerId;
    private readonly DateTimeOffset _startedAt;

    public Worker(
        ILogger<Worker> logger,
        IProcessingJobClaimQueue queue,
        IProcessingJobRecoveryQueue recoveryQueue,
        IWorkerHeartbeatStore heartbeatStore,
        WorkerJobProcessor processor,
        IEnumerable<IWorkerEndpointMetricsProvider> endpointMetricsProviders,
        IEnumerable<IWorkerEndpointConfigurationRefresher> endpointConfigurationRefreshers,
        WorkerRecoveryOptions? recoveryOptions = null)
    {
        _logger = logger;
        _queue = queue;
        _recoveryQueue = recoveryQueue;
        _heartbeatStore = heartbeatStore;
        _endpointMetricsProviders = endpointMetricsProviders.ToArray();
        _endpointConfigurationRefreshers = endpointConfigurationRefreshers.ToArray();
        _processor = processor;
        _recoveryOptions = recoveryOptions ?? new WorkerRecoveryOptions();
        _recoveryOptions.Validate();
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        _startedAt = DateTimeOffset.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CenteralES worker started.");

        var nextHeartbeatAt = DateTimeOffset.MinValue;
        var nextRecoveryAt = DateTimeOffset.MinValue;
        var nextEndpointConfigurationRefreshAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextEndpointConfigurationRefreshAt)
            {
                await RefreshEndpointConfigurationsAsync(stoppingToken);
                nextEndpointConfigurationRefreshAt = now.Add(EndpointConfigurationRefreshInterval);
            }

            if (now >= nextHeartbeatAt)
            {
                await _heartbeatStore.HeartbeatAsync(
                    new HeartbeatWorkerCommand(
                        _workerId,
                        PdfStampRecognitionConstants.ProcessorKey,
                        PdfStampRecognitionConstants.Capability,
                        _startedAt,
                        now,
                        CollectEndpointMetrics()),
                    stoppingToken);
                _logger.LogInformation("Worker {WorkerId} heartbeat at {HeartbeatAt}.", _workerId, now);
                nextHeartbeatAt = now.Add(HeartbeatInterval);
            }

            if (_recoveryOptions.Enabled && now >= nextRecoveryAt)
            {
                var recovered = await _recoveryQueue.RecoverStaleProcessingJobsAsync(
                    new RecoverStaleProcessingJobsCommand(
                        PdfStampRecognitionConstants.Capability,
                        now.Subtract(_recoveryOptions.StaleJobTimeout),
                        now,
                        _recoveryOptions.BatchSize),
                    stoppingToken);
                if (recovered > 0)
                {
                    _logger.LogWarning("Recovered {RecoveredJobCount} stale processing jobs back to queue.", recovered);
                }

                nextRecoveryAt = now.Add(_recoveryOptions.RecoveryInterval);
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

    private IReadOnlyList<WorkerEndpointMetric> CollectEndpointMetrics()
    {
        return _endpointMetricsProviders
            .SelectMany(provider => provider.GetEndpointMetrics())
            .ToArray();
    }

    private async Task RefreshEndpointConfigurationsAsync(CancellationToken stoppingToken)
    {
        foreach (var refresher in _endpointConfigurationRefreshers)
        {
            try
            {
                await refresher.RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to refresh processor endpoint configuration. Last valid snapshot remains active.");
            }
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
            try
            {
                await _queue.RefreshHeartbeatAsync(
                    new RefreshProcessingJobHeartbeatCommand(jobId, now),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to refresh heartbeat for job {JobId}.", jobId);
            }
        }
    }
}

public sealed class WorkerRecoveryOptions
{
    public bool Enabled { get; init; } = true;
    public TimeSpan StaleJobTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RecoveryInterval { get; init; } = TimeSpan.FromMinutes(1);
    public int BatchSize { get; init; } = 50;

    public void Validate()
    {
        if (StaleJobTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker recovery staleJobTimeout must be greater than zero.");
        }

        if (RecoveryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Worker recovery recoveryInterval must be greater than zero.");
        }

        if (BatchSize <= 0)
        {
            throw new InvalidOperationException("Worker recovery batchSize must be greater than zero.");
        }
    }
}
