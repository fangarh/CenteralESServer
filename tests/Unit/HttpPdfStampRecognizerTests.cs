using System.Net;
using System.Text;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;

namespace CenteralES.UnitTests;

public sealed class HttpPdfStampRecognizerTests
{
    [Fact]
    public async Task RecognizeAsync_posts_pdf_as_multipart_and_returns_raw_success_json()
    {
        HttpRequestMessage? sentRequest = null;
        var handler = new RecordingHttpMessageHandler(async (request, cancellationToken) =>
        {
            sentRequest = request;
            var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            Assert.Contains("Content-Type: application/pdf", requestBody, StringComparison.Ordinal);
            Assert.Contains("%PDF unit test", requestBody, StringComparison.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"items":[]}""", Encoding.UTF8, "application/json")
            };
        });
        var recognizer = CreateRecognizer(handler);
        var job = CreateJob();

        await using var pdf = new MemoryStream(Encoding.UTF8.GetBytes("%PDF unit test"));
        var result = await recognizer.RecognizeAsync(job, pdf, CancellationToken.None);

        Assert.Equal("""{"ok":true,"items":[]}""", result.PayloadJson);
        Assert.Equal("pdf2txt-recognize-json-v1", result.ContractVersion);
        Assert.Equal("https://pdf2txt.local/recognize_json/", result.Diagnostics.Endpoint);
        Assert.Equal(200, result.Diagnostics.HttpStatus);
        Assert.NotNull(sentRequest);
        Assert.True(sentRequest.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task RecognizeAsync_normalizes_http_errors_without_storing_raw_external_body()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("secret stack trace from external service")
            }));
        var recognizer = CreateRecognizer(handler);

        await using var pdf = new MemoryStream(Encoding.UTF8.GetBytes("%PDF unit test"));
        var exception = await Assert.ThrowsAsync<PdfStampRecognitionAdapterException>(
            () => recognizer.RecognizeAsync(CreateJob(), pdf, CancellationToken.None));

        Assert.Equal(NormalizedProcessorError.ProcessorHttpError, exception.Error);
        Assert.Equal(500, exception.Diagnostics.HttpStatus);
        Assert.DoesNotContain("secret", exception.Diagnostics.RawErrorExcerpt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecognizeAsync_treats_successful_json_with_errors_as_invalid_input()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"encoding":"utf-8","errors":["external validation detail"],"workers":[]}""",
                    Encoding.UTF8,
                    "application/json")
            }));
        var recognizer = CreateRecognizer(handler);

        await using var pdf = new MemoryStream(Encoding.UTF8.GetBytes("%PDF invalid"));
        var exception = await Assert.ThrowsAsync<PdfStampRecognitionAdapterException>(
            () => recognizer.RecognizeAsync(CreateJob(), pdf, CancellationToken.None));

        Assert.Equal(NormalizedProcessorError.InvalidInput, exception.Error);
        Assert.Equal(200, exception.Diagnostics.HttpStatus);
        Assert.DoesNotContain("external validation detail", exception.Diagnostics.RawErrorExcerpt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecognizeAsync_reports_overloaded_when_pool_has_no_available_capacity()
    {
        var options = new HttpPdfStampRecognizerOptions
        {
            EndpointPool = new[] { "https://pdf2txt.local/recognize_json/" },
            PoolConcurrencyLimit = 1,
            EndpointConcurrencyLimit = 1,
            Timeout = TimeSpan.FromSeconds(5)
        };
        var pool = new HttpPdfStampRecognizerEndpointPool(options);
        using var lease = await pool.TryAcquireAsync(CancellationToken.None);
        var recognizer = new HttpPdfStampRecognizer(
            new HttpClient(new RecordingHttpMessageHandler((_, _) => throw new InvalidOperationException("should not call http"))),
            pool,
            options);

        await using var pdf = new MemoryStream(Encoding.UTF8.GetBytes("%PDF unit test"));
        var exception = await Assert.ThrowsAsync<PdfStampRecognitionAdapterException>(
            () => recognizer.RecognizeAsync(CreateJob(), pdf, CancellationToken.None));

        Assert.Equal(NormalizedProcessorError.ProcessorOverloaded, exception.Error);
        Assert.Null(exception.Diagnostics.Endpoint);
    }

    private static HttpPdfStampRecognizer CreateRecognizer(HttpMessageHandler handler)
    {
        var options = new HttpPdfStampRecognizerOptions
        {
            EndpointPool = new[] { "https://pdf2txt.local/recognize_json/" },
            PoolConcurrencyLimit = 2,
            EndpointConcurrencyLimit = 1,
            Timeout = TimeSpan.FromSeconds(5)
        };

        return new HttpPdfStampRecognizer(
            new HttpClient(handler),
            new HttpPdfStampRecognizerEndpointPool(options),
            options);
    }

    private static ClaimedProcessingJob CreateJob()
    {
        return new ClaimedProcessingJob(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            "incoming/test.pdf",
            1);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _send(request, cancellationToken);
        }
    }
}
