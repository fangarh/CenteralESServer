using System.Security.Cryptography;
using System.Text;

namespace CenteralES.Storage;

public enum ContentHashAlgorithm
{
    Sha256 = 0,
    GostR34112012_256 = 1
}

public interface IContentHasher
{
    Task<string> ComputeAsync(
        Stream stream,
        ContentHashAlgorithm algorithm,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContentHashValue>> ComputeAllAsync(
        Stream stream,
        CancellationToken cancellationToken = default);
}

public sealed class ContentHasher : IContentHasher
{
    public Task<string> ComputeAsync(
        Stream stream,
        ContentHashAlgorithm algorithm,
        CancellationToken cancellationToken = default)
    {
        return ContentHash.ComputeAsync(stream, algorithm, cancellationToken);
    }

    public Task<IReadOnlyList<ContentHashValue>> ComputeAllAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return ContentHash.ComputeAllAsync(stream, cancellationToken);
    }
}

public static class ContentHash
{
    public static Task<string> ComputeAsync(
        Stream stream,
        ContentHashAlgorithm algorithm,
        CancellationToken cancellationToken = default)
    {
        return algorithm switch
        {
            ContentHashAlgorithm.Sha256 => ComputeSha256Async(stream, cancellationToken),
            ContentHashAlgorithm.GostR34112012_256 => ComputeGostR34112012_256Async(stream, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported content hash algorithm.")
        };
    }

    public static async Task<IReadOnlyList<ContentHashValue>> ComputeAllAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var streebog256 = new Streebog256DigestAdapter();
        var buffer = new byte[64 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            sha256.AppendData(buffer.AsSpan(0, read));
            streebog256.AppendData(buffer, read);
        }

        var sha256Hash = sha256.GetHashAndReset();
        var gostHash = streebog256.GetHashAndReset();

        return
        [
            new ContentHashValue(
                ContentHashAlgorithm.Sha256,
                ContentHashAlgorithms.Sha256,
                $"{ContentHashAlgorithms.Sha256}:{Convert.ToHexString(sha256Hash).ToLowerInvariant()}"),
            new ContentHashValue(
                ContentHashAlgorithm.GostR34112012_256,
                ContentHashAlgorithms.GostR34112012_256,
                $"{ContentHashAlgorithms.GostR34112012_256}:{Convert.ToHexString(gostHash).ToLowerInvariant()}")
        ];
    }

    public static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return $"{ContentHashAlgorithms.Sha256}:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static async Task<string> ComputeGostR34112012_256Async(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var hash = await Streebog256.HashAsync(stream, cancellationToken);
        return $"{ContentHashAlgorithms.GostR34112012_256}:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static string ToTemporaryStorageKeySegment(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var builder = new StringBuilder(contentHash.Length);
        foreach (var character in contentHash)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        return builder.ToString();
    }
}

public static class ContentHashAlgorithms
{
    public const string Sha256 = "sha256";
    public const string GostR34112012_256 = "gost-r-34.11-2012-256";

    public static bool TryParse(string? value, out ContentHashAlgorithm algorithm)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            algorithm = default;
            return false;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, Sha256, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, nameof(ContentHashAlgorithm.Sha256), StringComparison.OrdinalIgnoreCase))
        {
            algorithm = ContentHashAlgorithm.Sha256;
            return true;
        }

        if (string.Equals(normalized, GostR34112012_256, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "gost34112012-256", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "gost34112012_256", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "streebog-256", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "streebog256", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, nameof(ContentHashAlgorithm.GostR34112012_256), StringComparison.OrdinalIgnoreCase))
        {
            algorithm = ContentHashAlgorithm.GostR34112012_256;
            return true;
        }

        algorithm = default;
        return false;
    }

    public static string ToWireValue(ContentHashAlgorithm algorithm)
    {
        return algorithm switch
        {
            ContentHashAlgorithm.Sha256 => Sha256,
            ContentHashAlgorithm.GostR34112012_256 => GostR34112012_256,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported content hash algorithm.")
        };
    }
}
