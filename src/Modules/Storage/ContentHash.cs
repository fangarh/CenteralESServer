using System.Security.Cryptography;

namespace CenteralES.Storage;

public static class ContentHash
{
    public static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
