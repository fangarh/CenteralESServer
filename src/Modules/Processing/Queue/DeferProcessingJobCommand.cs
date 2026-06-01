namespace CenteralES.Processing.Queue;

public sealed record DeferProcessingJobCommand(
    Guid JobId,
    Guid SubjectId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset DeferredAt);
