namespace CenteralES.Processing.Queue;

public sealed record CompleteProcessingJobCommand(
    Guid JobId,
    Guid SubjectId,
    Guid ResultId,
    AttemptDiagnostics Diagnostics,
    DateTimeOffset FinishedAt);
