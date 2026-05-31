using CenteralES.Processing.Queue;

namespace CenteralES.Worker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private readonly ILogger<Worker> _logger;
    private readonly IProcessingJobQueue _queue;
    private readonly WorkerJobProcessor _processor;

    public Worker(
        ILogger<Worker> logger,
        IProcessingJobQueue queue,
        WorkerJobProcessor processor)
    {
        _logger = logger;
        _queue = queue;
        _processor = processor;
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

            await _processor.ProcessAsync(job, stoppingToken);
        }
    }
}
