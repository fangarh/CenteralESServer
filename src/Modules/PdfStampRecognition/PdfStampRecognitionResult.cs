namespace CenteralES.PdfStampRecognition;

public sealed record PdfStampRecognitionResult(
    Guid ResultIndexId,
    Guid PayloadId,
    Guid SubjectId,
    Guid JobId,
    string ContentHash,
    string PayloadJson,
    string ContractVersion,
    long PayloadSize,
    DateTimeOffset CreatedAt);
