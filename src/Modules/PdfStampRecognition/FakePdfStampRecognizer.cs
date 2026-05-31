using System.Diagnostics;
using System.Text.Json;
using CenteralES.Processing;
using CenteralES.Processing.Queue;

namespace CenteralES.PdfStampRecognition;

public sealed class FakePdfStampRecognizer : IPdfStampRecognizer
{
    public async Task<PdfStampRecognitionAdapterResult> RecognizeAsync(
        ClaimedProcessingJob job,
        Stream pdfContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(pdfContent);

        var stopwatch = Stopwatch.StartNew();
        var byteCount = await CountBytesAsync(pdfContent, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        var payload = JsonSerializer.Serialize(new
        {
            source = "fake-pdf2txt",
            capability = PdfStampRecognitionConstants.Capability,
            jobId = job.JobId,
            contentHash = job.ContentHash,
            inputBytes = byteCount,
            people = Array.Empty<object>()
        });

        return new PdfStampRecognitionAdapterResult(
            payload,
            "fake-v1",
            new AttemptDiagnostics(
                Endpoint: "fake://pdf2txt-http-recognizer",
                Duration: stopwatch.Elapsed,
                HttpStatus: 200,
                NormalizedError: null,
                Retryable: null,
                CorrelationId: Guid.NewGuid().ToString("N")));
    }

    private static async Task<long> CountBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
        }

        return total;
    }
}
