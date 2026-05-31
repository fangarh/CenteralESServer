using System.Diagnostics;
using System.Text.Json;
using CenteralES.Processing;
using CenteralES.Processing.Queue;

namespace CenteralES.PdfStampRecognition;

public sealed class FakePdfStampRecognizer : IPdfStampRecognizer
{
    public async Task<PdfStampRecognitionAdapterResult> RecognizeAsync(ClaimedProcessingJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var stopwatch = Stopwatch.StartNew();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        var payload = JsonSerializer.Serialize(new
        {
            source = "fake-pdf2txt",
            capability = PdfStampRecognitionConstants.Capability,
            jobId = job.JobId,
            contentHash = job.ContentHash,
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
}
