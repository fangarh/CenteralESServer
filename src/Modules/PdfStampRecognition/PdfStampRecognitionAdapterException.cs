using CenteralES.Processing;

namespace CenteralES.PdfStampRecognition;

public sealed class PdfStampRecognitionAdapterException : Exception
{
    public PdfStampRecognitionAdapterException(
        NormalizedProcessorError error,
        AttemptDiagnostics diagnostics,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        Diagnostics = diagnostics;
    }

    public NormalizedProcessorError Error { get; }
    public AttemptDiagnostics Diagnostics { get; }
}
