namespace CenteralES.Processing;

public enum ProcessorEndpointHealth
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

public sealed record ProcessorEndpointSnapshot(
    string Url,
    bool Enabled,
    ProcessorEndpointHealth Health,
    int InFlight,
    int ConcurrencyLimit);

public sealed record SelectedProcessorEndpoint(string Url, int InFlight);

public static class EndpointPoolSelector
{
    public static SelectedProcessorEndpoint? SelectLeastInFlight(IEnumerable<ProcessorEndpointSnapshot> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .Where(endpoint => endpoint.Enabled)
            .Where(endpoint => endpoint.Health is ProcessorEndpointHealth.Healthy or ProcessorEndpointHealth.Unknown)
            .Where(endpoint => endpoint.InFlight < endpoint.ConcurrencyLimit)
            .OrderBy(endpoint => endpoint.InFlight)
            .ThenBy(endpoint => endpoint.Url, StringComparer.Ordinal)
            .Select(endpoint => new SelectedProcessorEndpoint(endpoint.Url, endpoint.InFlight))
            .FirstOrDefault();
    }
}
