namespace CenteralES.Storage;

public sealed record ContentHashValue(
    ContentHashAlgorithm Algorithm,
    string AlgorithmName,
    string Value);
