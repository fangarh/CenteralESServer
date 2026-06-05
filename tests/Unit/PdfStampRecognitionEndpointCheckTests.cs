using System.Net;
using System.Net.Sockets;
using System.Text;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing;

namespace CenteralES.UnitTests;

public sealed class PdfStampRecognitionEndpointCheckTests
{
    [Fact]
    public async Task CheckAsync_posts_configured_sample_pdf_as_multipart_without_returning_payload()
    {
        var samplePath = Path.Combine(Path.GetTempPath(), $"centerales-check-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(samplePath, "%PDF diagnostic sample");
        HttpRequestMessage? sentRequest = null;
        try
        {
            var handler = new RecordingHttpMessageHandler(async (request, cancellationToken) =>
            {
                sentRequest = request;
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                var filePart = Assert.Single(multipart);
                Assert.Equal("file", filePart.Headers.ContentDisposition?.Name?.Trim('"'));
                Assert.Equal(Path.GetFileName(samplePath), filePart.Headers.ContentDisposition?.FileName?.Trim('"'));
                var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("%PDF diagnostic sample", requestBody, StringComparison.Ordinal);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"secretPayload":true}""", Encoding.UTF8, "application/json")
                };
            });
            var checker = CreateChecker(handler, samplePath);

            var result = await checker.CheckAsync(
                new PdfStampRecognitionEndpointCheckCommand(
                    "https://user:password@pdf2txt.local/recognize_json/?token=hidden#frag",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);

            Assert.Equal(PdfStampRecognitionEndpointCheckStatus.Succeeded, result.Status);
            Assert.Equal("https://pdf2txt.local/recognize_json/", result.Endpoint);
            Assert.Equal(200, result.HttpStatus);
            Assert.True(result.ResponseSizeBytes > 0);
            Assert.Null(result.RawResponseExcerpt);
            Assert.NotNull(sentRequest);
            Assert.Equal(new Uri("https://user:password@pdf2txt.local/recognize_json/?token=hidden#frag"), sentRequest.RequestUri);
            Assert.Contains("application/json", sentRequest.Headers.Accept.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(samplePath);
        }
    }

    [Fact]
    public async Task CheckAsync_returns_safe_network_failure_detail()
    {
        var samplePath = Path.Combine(Path.GetTempPath(), $"centerales-check-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(samplePath, "%PDF diagnostic sample");
        try
        {
            var handler = new RecordingHttpMessageHandler((_, _) =>
                throw new HttpRequestException(
                    "Connection failed.",
                    new SocketException((int)SocketError.ConnectionRefused)));
            var checker = CreateChecker(handler, samplePath);

            var result = await checker.CheckAsync(
                new PdfStampRecognitionEndpointCheckCommand(
                    "https://pdf2txt.local/recognize_json/",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);

            Assert.Equal(PdfStampRecognitionEndpointCheckStatus.Failed, result.Status);
            Assert.Equal(NormalizedProcessorError.ProcessorUnreachable, result.NormalizedError);
            Assert.Contains("SocketException", result.RawResponseExcerpt, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(samplePath);
        }
    }

    [Fact]
    public async Task CheckAsync_rejects_missing_sample_pdf_without_http_call()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP should not be called when the sample PDF is missing."));
        var checker = CreateChecker(handler, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf"));

        var result = await checker.CheckAsync(
            new PdfStampRecognitionEndpointCheckCommand(
                "https://pdf2txt.local/recognize_json/",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(PdfStampRecognitionEndpointCheckStatus.NotConfigured, result.Status);
        Assert.Equal("Diagnostic sample PDF is not configured or not readable.", result.RawResponseExcerpt);
    }

    private static PdfStampRecognitionEndpointChecker CreateChecker(HttpMessageHandler handler, string samplePath)
    {
        return new PdfStampRecognitionEndpointChecker(
            new RecordingHttpClientFactory(handler),
            new PdfStampRecognitionEndpointCheckOptions(
                samplePath,
                Timeout: TimeSpan.FromSeconds(5)));
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

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public RecordingHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PdfStampRecognitionEndpointChecker.HttpClientName, name);
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}
