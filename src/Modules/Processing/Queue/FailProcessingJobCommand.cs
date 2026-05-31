namespace CenteralES.Processing.Queue;

public sealed record FailProcessingJobCommand(
    Guid JobId,
    Guid SubjectId,
    NormalizedProcessorError Error,
    bool Final,
    AttemptDiagnostics Diagnostics,
    DateTimeOffset FinishedAt);
