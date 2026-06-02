namespace CenteralES.Processing.Queue;

public sealed record ProcessingContentHash(string Algorithm, string HashValue);

public sealed record CreateProcessingJobCommand(
    string Capability,
    string ContentHash,
    string TemporaryFileKey,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProcessingContentHash>? ContentHashes = null);
