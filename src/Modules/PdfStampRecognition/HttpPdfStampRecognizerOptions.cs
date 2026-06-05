namespace CenteralES.PdfStampRecognition;

public sealed class HttpPdfStampRecognizerOptions
{
    public IReadOnlyList<string> EndpointPool { get; init; } = Array.Empty<string>();
    public int PoolConcurrencyLimit { get; init; } = 1;
    public int EndpointConcurrencyLimit { get; init; } = 1;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public string ContractVersion { get; init; } = "pdf2txt-recognize-json-v1";
    public string? ProxyUrl { get; init; }
    public bool DisableEnvironmentProxy { get; init; }

    public void Validate()
    {
        if (EndpointPool.Count == 0)
        {
            throw new InvalidOperationException("PdfStampRecognition processor endpointPool must contain at least one endpoint.");
        }

        if (PoolConcurrencyLimit <= 0)
        {
            throw new InvalidOperationException("PdfStampRecognition processor poolConcurrencyLimit must be greater than zero.");
        }

        if (EndpointConcurrencyLimit <= 0)
        {
            throw new InvalidOperationException("PdfStampRecognition processor endpointConcurrencyLimit must be greater than zero.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("PdfStampRecognition processor timeout must be greater than zero.");
        }

        foreach (var endpoint in EndpointPool)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException($"PdfStampRecognition processor endpoint '{endpoint}' must be an absolute URI.");
            }
        }

        if (!string.IsNullOrWhiteSpace(ProxyUrl)
            && !Uri.TryCreate(ProxyUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("PdfStampRecognition processor proxyUrl must be an absolute URI.");
        }

        if (!string.IsNullOrWhiteSpace(ProxyUrl) && DisableEnvironmentProxy)
        {
            throw new InvalidOperationException("PdfStampRecognition processor proxyUrl cannot be combined with disableEnvironmentProxy.");
        }
    }
}
