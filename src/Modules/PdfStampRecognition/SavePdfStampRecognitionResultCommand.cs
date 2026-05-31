namespace CenteralES.PdfStampRecognition;

public sealed record SavePdfStampRecognitionResultCommand(
    Guid SubjectId,
    Guid JobId,
    string ContentHash,
    string PayloadJson,
    string ContractVersion,
    DateTimeOffset CreatedAt);
