namespace CenteralES.PdfStampRecognition;

public interface IPdfStampRecognitionResultStore
{
    Task<PdfStampRecognitionResult> SaveAsync(SavePdfStampRecognitionResultCommand command, CancellationToken cancellationToken);

    Task<PdfStampRecognitionResult?> GetByHashAsync(string contentHash, CancellationToken cancellationToken);
}
