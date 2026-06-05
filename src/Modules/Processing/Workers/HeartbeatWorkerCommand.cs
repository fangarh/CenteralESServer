namespace CenteralES.Processing.Workers;

public sealed record HeartbeatWorkerCommand(
    string WorkerId,
    string ProcessorKey,
    string Capability,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,
    IReadOnlyList<WorkerEndpointMetric>? EndpointMetrics = null);

public sealed record WorkerEndpointMetric(
    string Endpoint,
    bool Enabled,
    string Health,
    int InFlight,
    int ConcurrencyLimit);

public interface IWorkerEndpointMetricsProvider
{
    IReadOnlyList<WorkerEndpointMetric> GetEndpointMetrics();
}

public interface IWorkerEndpointConfigurationRefresher
{
    Task RefreshAsync(CancellationToken cancellationToken);
}
