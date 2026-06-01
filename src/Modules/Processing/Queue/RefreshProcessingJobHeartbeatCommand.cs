namespace CenteralES.Processing.Queue;

public sealed record RefreshProcessingJobHeartbeatCommand(
    Guid JobId,
    DateTimeOffset HeartbeatAt);
