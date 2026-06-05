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

public sealed record ProcessorEndpointConfiguration(
    string Endpoint,
    bool Enabled,
    int ConcurrencyLimit,
    string Source);

public interface IProcessorEndpointConfigurationStore
{
    Task<IReadOnlyList<ProcessorEndpointConfiguration>> ListProcessorEndpointsAsync(
        string processorKey,
        string capability,
        CancellationToken cancellationToken);
}

public static class ProcessorEndpointConfigurationMerger
{
    public static IReadOnlyList<ProcessorEndpointConfiguration> MergeEnvAndDatabaseEndpoints(
        IReadOnlyList<string> envEndpoints,
        int envConcurrencyLimit,
        IReadOnlyList<ProcessorEndpointConfiguration> databaseEndpoints)
    {
        ArgumentNullException.ThrowIfNull(envEndpoints);
        ArgumentNullException.ThrowIfNull(databaseEndpoints);

        if (envConcurrencyLimit <= 0)
        {
            throw new InvalidOperationException("Env processor endpoint concurrency limit must be greater than zero.");
        }

        var databaseByNormalizedEndpoint = databaseEndpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.Endpoint))
            .GroupBy(endpoint => ProcessorEndpointNormalizer.Normalize(endpoint.Endpoint), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var merged = new List<ProcessorEndpointConfiguration>();
        foreach (var envEndpoint in envEndpoints.Where(endpoint => !string.IsNullOrWhiteSpace(endpoint)).Distinct(StringComparer.Ordinal))
        {
            var normalized = ProcessorEndpointNormalizer.Normalize(envEndpoint);
            if (databaseByNormalizedEndpoint.ContainsKey(normalized))
            {
                continue;
            }

            merged.Add(new ProcessorEndpointConfiguration(envEndpoint, Enabled: true, envConcurrencyLimit, "env"));
        }

        merged.AddRange(databaseByNormalizedEndpoint.Values.Where(endpoint => endpoint.Enabled));

        return merged
            .OrderBy(endpoint => endpoint.Source == "env" ? 0 : 1)
            .ThenBy(endpoint => ProcessorEndpointNormalizer.Normalize(endpoint.Endpoint), StringComparer.Ordinal)
            .ToArray();
    }
}

public static class ProcessorEndpointNormalizer
{
    public static string Normalize(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Processor endpoint must be an absolute URI.");
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
    }
}

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
