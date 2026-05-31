namespace CenteralES.Processing;

public sealed record AttemptDiagnostics(
    string? Endpoint,
    TimeSpan? Duration,
    int? HttpStatus,
    NormalizedProcessorError? NormalizedError,
    bool? Retryable,
    string CorrelationId,
    string? RawErrorExcerpt = null);
