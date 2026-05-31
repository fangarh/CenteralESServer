namespace CenteralES.Processing.Queue;

public sealed record EnqueueProcessingJobResult(
    Guid SubjectId,
    Guid JobId,
    int AttemptNumber,
    ProcessingJobStatus Status,
    bool Deduplicated);
