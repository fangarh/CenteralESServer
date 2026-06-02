namespace CenteralES.Processing.Queue;

public sealed record RecoverStaleProcessingJobsCommand(
    string Capability,
    DateTimeOffset StaleBefore,
    DateTimeOffset RecoveredAt,
    int Limit);
