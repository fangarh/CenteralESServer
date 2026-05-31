namespace CenteralES.Processing.Queue;

public sealed record ClaimedProcessingJob(
    Guid JobId,
    Guid SubjectId,
    string Capability,
    string ContentHash,
    string TemporaryFileKey,
    int AttemptNumber);
