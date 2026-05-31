namespace CenteralES.Worker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CenteralES worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker heartbeat at {HeartbeatAt}. Queue processing is not wired yet.", DateTimeOffset.UtcNow);
            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }
}
