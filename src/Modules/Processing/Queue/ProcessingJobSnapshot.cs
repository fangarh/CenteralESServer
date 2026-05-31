namespace CenteralES.Processing.Queue;

public sealed record ProcessingJobSnapshot(
    Guid SubjectId,
    Guid JobId,
    string Capability,
    string ContentHash,
    string TemporaryFileKey,
    int AttemptNumber,
    ProcessingJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    AttemptDiagnostics? Diagnostics);
