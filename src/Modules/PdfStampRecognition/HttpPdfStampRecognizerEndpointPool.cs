using CenteralES.Processing;

namespace CenteralES.PdfStampRecognition;

public sealed class HttpPdfStampRecognizerEndpointPool
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly Dictionary<string, EndpointState> _endpoints;
    private readonly int _endpointConcurrencyLimit;

    public HttpPdfStampRecognizerEndpointPool(HttpPdfStampRecognizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _poolSemaphore = new SemaphoreSlim(options.PoolConcurrencyLimit, options.PoolConcurrencyLimit);
        _endpointConcurrencyLimit = options.EndpointConcurrencyLimit;
        _endpoints = options.EndpointPool
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                endpoint => endpoint,
                endpoint => new EndpointState(endpoint),
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
                    Enabled: true,
                    endpoint.Health,
                    endpoint.InFlight,
                    _endpointConcurrencyLimit)));

            if (selected is null)
            {
                _poolSemaphore.Release();
                return null;
            }

            _endpoints[selected.Url].InFlight++;
            return new HttpPdfStampRecognizerEndpointLease(selected.Url, Release);
        }
    }

    private void Release(string endpoint)
    {
        lock (_gate)
        {
            _endpoints[endpoint].InFlight--;
        }

        _poolSemaphore.Release();
    }

    private sealed class EndpointState
    {
        public EndpointState(string url)
        {
            Url = url;
        }

        public string Url { get; }
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
