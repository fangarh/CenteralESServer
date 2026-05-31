using CenteralES.Processing.Queue;

namespace CenteralES.PdfStampRecognition;

public interface IPdfStampRecognizer
{
    Task<PdfStampRecognitionAdapterResult> RecognizeAsync(
        ClaimedProcessingJob job,
        Stream pdfContent,
        CancellationToken cancellationToken);
}
