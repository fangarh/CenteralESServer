using CenteralES.Processing;
using CenteralES.Processing.Workers;

namespace CenteralES.PdfStampRecognition;

public sealed class HttpPdfStampRecognizerEndpointPool : IWorkerEndpointMetricsProvider
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly Dictionary<string, EndpointState> _endpoints;

    public HttpPdfStampRecognizerEndpointPool(HttpPdfStampRecognizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _poolSemaphore = new SemaphoreSlim(options.PoolConcurrencyLimit, options.PoolConcurrencyLimit);
        _endpoints = options.EndpointPool
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                endpoint => endpoint,
                endpoint => new EndpointState(endpoint, enabled: true, options.EndpointConcurrencyLimit),
                StringComparer.Ordinal);
    }

    public async ValueTask<HttpPdfStampRecognizerEndpointLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _poolSemaphore.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        lock (_gate)
        {
            var selected = EndpointPoolSelector.SelectLeastInFlight(
                _endpoints.Values.Select(endpoint => new ProcessorEndpointSnapshot(
                    endpoint.Url,
                    endpoint.Enabled,
                    endpoint.Health,
                    endpoint.InFlight,
                    endpoint.ConcurrencyLimit)));

            if (selected is null)
            {
                _poolSemaphore.Release();
                return null;
            }

            _endpoints[selected.Url].InFlight++;
            return new HttpPdfStampRecognizerEndpointLease(selected.Url, Release);
        }
    }

    public IReadOnlyList<WorkerEndpointMetric> GetEndpointMetrics()
    {
        lock (_gate)
        {
            return _endpoints.Values
                .OrderBy(endpoint => endpoint.Url, StringComparer.Ordinal)
                .Select(endpoint => new WorkerEndpointMetric(
                    endpoint.Url,
                    endpoint.Enabled,
                    endpoint.Health.ToString().ToLowerInvariant(),
                    endpoint.InFlight,
                    endpoint.ConcurrencyLimit))
                .ToArray();
        }
    }

    public void UpdateEndpoints(IReadOnlyList<ProcessorEndpointConfiguration> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var normalized = endpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.Endpoint))
            .GroupBy(endpoint => endpoint.Endpoint, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        foreach (var endpoint in normalized)
        {
            if (endpoint.ConcurrencyLimit <= 0)
            {
                throw new InvalidOperationException("Processor endpoint concurrency limit must be greater than zero.");
            }
        }

        lock (_gate)
        {
            var configured = normalized
                .Select(endpoint => endpoint.Endpoint)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var endpoint in normalized)
            {
                if (_endpoints.TryGetValue(endpoint.Endpoint, out var existing))
                {
                    existing.Enabled = endpoint.Enabled;
                    existing.ConcurrencyLimit = endpoint.ConcurrencyLimit;
                    existing.RemoveWhenIdle = false;
                    continue;
                }

                _endpoints.Add(
                    endpoint.Endpoint,
                    new EndpointState(endpoint.Endpoint, endpoint.Enabled, endpoint.ConcurrencyLimit));
            }

            foreach (var endpoint in _endpoints.Values.ToArray())
            {
                if (configured.Contains(endpoint.Url))
                {
                    continue;
                }

                if (endpoint.InFlight > 0)
                {
                    endpoint.Enabled = false;
                    endpoint.RemoveWhenIdle = true;
                    continue;
                }

                _endpoints.Remove(endpoint.Url);
            }
        }
    }

    private void Release(string endpoint)
    {
        lock (_gate)
        {
            if (_endpoints.TryGetValue(endpoint, out var state))
            {
                state.InFlight = Math.Max(0, state.InFlight - 1);
                if (state.RemoveWhenIdle && state.InFlight == 0)
                {
                    _endpoints.Remove(endpoint);
                }
            }
        }

        _poolSemaphore.Release();
    }

    private sealed class EndpointState
    {
        public EndpointState(string url, bool enabled, int concurrencyLimit)
        {
            Url = url;
            Enabled = enabled;
            ConcurrencyLimit = concurrencyLimit;
        }

        public string Url { get; }
        public bool Enabled { get; set; }
        public int ConcurrencyLimit { get; set; }
        public bool RemoveWhenIdle { get; set; }
        public ProcessorEndpointHealth Health { get; } = ProcessorEndpointHealth.Unknown;
        public int InFlight { get; set; }
    }
}

public sealed class HttpPdfStampRecognizerEndpointLease : IDisposable
{
    private readonly Action<string> _release;
    private bool _disposed;

    public HttpPdfStampRecognizerEndpointLease(string endpoint, Action<string> release)
    {
        Endpoint = endpoint;
        _release = release;
    }

    public string Endpoint { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _release(Endpoint);
    }
}
