using CenteralES.Processing;

namespace CenteralES.PdfStampRecognition;

public sealed record PdfStampRecognitionAdapterResult(
    string PayloadJson,
    string ContractVersion,
    AttemptDiagnostics Diagnostics);
