namespace CenteralES.Processing.Queue;

public sealed record CreateProcessingJobCommand(
    string Capability,
    string ContentHash,
    string TemporaryFileKey,
    DateTimeOffset CreatedAt);
