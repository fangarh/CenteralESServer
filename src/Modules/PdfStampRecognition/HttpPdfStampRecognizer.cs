using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CenteralES.Processing;
using CenteralES.Processing.Queue;

namespace CenteralES.PdfStampRecognition;

public sealed class HttpPdfStampRecognizer : IPdfStampRecognizer
{
    public const string HttpClientName = "PdfStampRecognizer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpPdfStampRecognizerEndpointPool _endpointPool;
    private readonly HttpPdfStampRecognizerOptions _options;

    public HttpPdfStampRecognizer(
        IHttpClientFactory httpClientFactory,
        HttpPdfStampRecognizerEndpointPool endpointPool,
        HttpPdfStampRecognizerOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _endpointPool = endpointPool;
        _options = options;
        _options.Validate();
    }

    public async Task<PdfStampRecognitionAdapterResult> RecognizeAsync(
        ClaimedProcessingJob job,
        Stream pdfContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(pdfContent);

        var correlationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        using var lease = await _endpointPool.TryAcquireAsync(cancellationToken);

        if (lease is null)
        {
            stopwatch.Stop();
            throw CreateException(
                NormalizedProcessorError.ProcessorOverloaded,
                endpoint: null,
                duration: stopwatch.Elapsed,
                httpStatus: null,
                correlationId,
                rawErrorExcerpt: "Processor endpoint pool is saturated.",
                message: "Processor endpoint pool is saturated.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            using var request = CreateRequest(lease.Endpoint, job, pdfContent, correlationId);
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                throw CreateException(
                    MapHttpStatus(response.StatusCode),
                    lease.Endpoint,
                    stopwatch.Elapsed,
                    (int)response.StatusCode,
                    correlationId,
                    $"Processor returned HTTP {(int)response.StatusCode}.",
                    "Processor returned an unsuccessful HTTP response.");
            }

            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            var payloadClassification = ClassifySuccessPayload(payload);
            if (payloadClassification is SuccessPayloadClassification.NotJson)
            {
                stopwatch.Stop();
                throw CreateException(
                    NormalizedProcessorError.ProcessorBadResponse,
                    lease.Endpoint,
                    stopwatch.Elapsed,
                    (int)response.StatusCode,
                    correlationId,
                    "Processor returned a non-JSON success payload.",
                    "Processor returned a non-JSON success payload.");
            }

            if (payloadClassification is SuccessPayloadClassification.ValidationErrors)
            {
                stopwatch.Stop();
                throw CreateException(
                    NormalizedProcessorError.InvalidInput,
                    lease.Endpoint,
                    stopwatch.Elapsed,
                    (int)response.StatusCode,
                    correlationId,
                    "Processor returned validation errors in a successful JSON response.",
                    "Processor returned validation errors in a successful JSON response.");
            }

            stopwatch.Stop();
            return new PdfStampRecognitionAdapterResult(
                payload,
                _options.ContractVersion,
                new AttemptDiagnostics(
                    lease.Endpoint,
                    stopwatch.Elapsed,
                    (int)response.StatusCode,
                    NormalizedError: null,
                    Retryable: null,
                    correlationId));
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw CreateException(
                NormalizedProcessorError.ProcessorTimeout,
                lease.Endpoint,
                stopwatch.Elapsed,
                httpStatus: null,
                correlationId,
                $"Processor request exceeded timeout {_options.Timeout}.",
                "Processor request timed out.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            throw CreateException(
                NormalizedProcessorError.ProcessorUnreachable,
                lease.Endpoint,
                stopwatch.Elapsed,
                httpStatus: null,
                correlationId,
                "Processor request failed before receiving an HTTP response.",
                "Processor request failed before receiving an HTTP response.",
                ex);
        }
    }

    private static HttpRequestMessage CreateRequest(
        string endpoint,
        ClaimedProcessingJob job,
        Stream pdfContent,
        string correlationId)
    {
        var streamContent = new StreamContent(pdfContent);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var hashSeparatorIndex = job.ContentHash.IndexOf(':', StringComparison.Ordinal);
        var hashFileName = hashSeparatorIndex >= 0
            ? job.ContentHash[(hashSeparatorIndex + 1)..]
            : job.ContentHash;
        var multipart = new MultipartFormDataContent
        {
            { streamContent, "file", $"{hashFileName}.pdf" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = multipart
        };
        request.Headers.Add("X-Correlation-Id", correlationId);

        return request;
    }

    private static SuccessPayloadClassification ClassifySuccessPayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind is JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                return SuccessPayloadClassification.ValidationErrors;
            }

            return SuccessPayloadClassification.ValidJson;
        }
        catch (JsonException)
        {
            return SuccessPayloadClassification.NotJson;
        }
    }

    private enum SuccessPayloadClassification
    {
        ValidJson,
        ValidationErrors,
        NotJson
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

    private static PdfStampRecognitionAdapterException CreateException(
        NormalizedProcessorError error,
        string? endpoint,
        TimeSpan? duration,
        int? httpStatus,
        string correlationId,
        string rawErrorExcerpt,
        string message,
        Exception? innerException = null)
    {
        var classification = ProcessorErrorClassifier.Classify(error);
        return new PdfStampRecognitionAdapterException(
            error,
            new AttemptDiagnostics(
                endpoint,
                duration,
                httpStatus,
                error,
                classification.IsRetryable,
                correlationId,
                rawErrorExcerpt),
            message,
            innerException);
    }
}
