namespace CenteralES.Processing.Workers;

public sealed record HeartbeatWorkerCommand(
    string WorkerId,
    string ProcessorKey,
    string Capability,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt);
