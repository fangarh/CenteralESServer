using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using CenteralES.Processing;

namespace CenteralES.PdfStampRecognition;

public sealed record PdfStampRecognitionEndpointCheckOptions(
    string? SamplePdfPath,
    TimeSpan Timeout);

public sealed record PdfStampRecognitionEndpointCheckCommand(
    string Endpoint,
    DateTimeOffset RequestedAt);

public enum PdfStampRecognitionEndpointCheckStatus
{
    Succeeded,
    Failed,
    NotConfigured
}

public sealed record PdfStampRecognitionEndpointCheckResult(
    PdfStampRecognitionEndpointCheckStatus Status,
    string Endpoint,
    DateTimeOffset CheckedAt,
    TimeSpan? Duration,
    int? HttpStatus,
    NormalizedProcessorError? NormalizedError,
    bool? Retryable,
    long? ResponseSizeBytes,
    string? RawResponseExcerpt);

public sealed class PdfStampRecognitionEndpointChecker
{
    public const string HttpClientName = "PdfStampRecognitionEndpointCheck";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PdfStampRecognitionEndpointCheckOptions _options;

    public PdfStampRecognitionEndpointChecker(
        IHttpClientFactory httpClientFactory,
        PdfStampRecognitionEndpointCheckOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("PdfStampRecognition endpoint check timeout must be greater than zero.");
        }
    }

    public async Task<PdfStampRecognitionEndpointCheckResult> CheckAsync(
        PdfStampRecognitionEndpointCheckCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sanitizedEndpoint = SanitizeEndpoint(command.Endpoint);
        if (string.IsNullOrWhiteSpace(_options.SamplePdfPath) || !File.Exists(_options.SamplePdfPath))
        {
            return new PdfStampRecognitionEndpointCheckResult(
                PdfStampRecognitionEndpointCheckStatus.NotConfigured,
                sanitizedEndpoint,
                command.RequestedAt,
                Duration: null,
                HttpStatus: null,
                NormalizedError: null,
                Retryable: null,
                ResponseSizeBytes: null,
                RawResponseExcerpt: "Diagnostic sample PDF is not configured or not readable.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var pdf = File.OpenRead(_options.SamplePdfPath);
            using var request = CreateRequest(command.Endpoint, pdf, Path.GetFileName(_options.SamplePdfPath));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var payload = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new PdfStampRecognitionEndpointCheckResult(
                    PdfStampRecognitionEndpointCheckStatus.Succeeded,
                    sanitizedEndpoint,
                    command.RequestedAt,
                    stopwatch.Elapsed,
                    (int)response.StatusCode,
                    NormalizedError: null,
                    Retryable: null,
                    payload.LongLength,
                    RawResponseExcerpt: null);
            }

            var error = MapHttpStatus(response.StatusCode);
            var classification = ProcessorErrorClassifier.Classify(error);
            return new PdfStampRecognitionEndpointCheckResult(
                PdfStampRecognitionEndpointCheckStatus.Failed,
                sanitizedEndpoint,
                command.RequestedAt,
                stopwatch.Elapsed,
                (int)response.StatusCode,
                error,
                classification.IsRetryable,
                payload.LongLength,
                $"Processor returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var error = NormalizedProcessorError.ProcessorTimeout;
            var classification = ProcessorErrorClassifier.Classify(error);
            return new PdfStampRecognitionEndpointCheckResult(
                PdfStampRecognitionEndpointCheckStatus.Failed,
                sanitizedEndpoint,
                command.RequestedAt,
                stopwatch.Elapsed,
                HttpStatus: null,
                error,
                classification.IsRetryable,
                ResponseSizeBytes: null,
                RawResponseExcerpt: $"Processor diagnostic check exceeded timeout {_options.Timeout}. {ex.GetType().Name}");
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var error = NormalizedProcessorError.ProcessorUnreachable;
            var classification = ProcessorErrorClassifier.Classify(error);
            return new PdfStampRecognitionEndpointCheckResult(
                PdfStampRecognitionEndpointCheckStatus.Failed,
                sanitizedEndpoint,
                command.RequestedAt,
                stopwatch.Elapsed,
                HttpStatus: null,
                error,
                classification.IsRetryable,
                ResponseSizeBytes: null,
                RawResponseExcerpt: CreateNetworkFailureExcerpt(ex));
        }
        catch (IOException)
        {
            stopwatch.Stop();
            return new PdfStampRecognitionEndpointCheckResult(
                PdfStampRecognitionEndpointCheckStatus.NotConfigured,
                sanitizedEndpoint,
                command.RequestedAt,
                Duration: null,
                HttpStatus: null,
                NormalizedError: null,
                Retryable: null,
                ResponseSizeBytes: null,
                RawResponseExcerpt: "Diagnostic sample PDF is not configured or not readable.");
        }
        catch (UnauthorizedAccessException)
        {
            stopwatch.Stop();
            return new PdfStampRecognitionEndpointCheckResult(
                PdfStampRecognitionEndpointCheckStatus.NotConfigured,
                sanitizedEndpoint,
                command.RequestedAt,
                Duration: null,
                HttpStatus: null,
                NormalizedError: null,
                Retryable: null,
                ResponseSizeBytes: null,
                RawResponseExcerpt: "Diagnostic sample PDF is not configured or not readable.");
        }
    }

    private static HttpRequestMessage CreateRequest(string endpoint, Stream pdfContent, string fileName)
    {
        var streamContent = new StreamContent(pdfContent);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var multipart = new MultipartFormDataContent
        {
            { streamContent, "file", fileName }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = multipart
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static NormalizedProcessorError MapHttpStatus(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest or
            HttpStatusCode.UnsupportedMediaType or
            HttpStatusCode.UnprocessableEntity => NormalizedProcessorError.InvalidInput,
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.GatewayTimeout => NormalizedProcessorError.ProcessorTimeout,
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.ServiceUnavailable => NormalizedProcessorError.ProcessorOverloaded,
            _ => NormalizedProcessorError.ProcessorHttpError
        };
    }

    private static string SanitizeEndpoint(string endpoint)
    {
        try
        {
            return ProcessorEndpointNormalizer.Normalize(endpoint);
        }
        catch (InvalidOperationException)
        {
            return "invalid-endpoint";
        }
    }

    private static string CreateNetworkFailureExcerpt(HttpRequestException exception)
    {
        Exception root = exception;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        var message = string.IsNullOrWhiteSpace(root.Message)
            ? exception.Message
            : root.Message;

        return TrimDiagnostic($"Processor request failed before receiving an HTTP response. Root={root.GetType().Name}: {message}");
    }

    private static string TrimDiagnostic(string value)
    {
        const int maxLength = 500;
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
