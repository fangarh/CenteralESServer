using System.Buffers;
using Org.BouncyCastle.Crypto.Digests;

namespace CenteralES.Storage;

internal static class Streebog256
{
    public static async Task<byte[]> HashAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var digest = new Gost3411_2012_256Digest();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                digest.BlockUpdate(buffer, 0, read);
            }

            var result = new byte[digest.GetDigestSize()];
            digest.DoFinal(result, 0);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal sealed class Streebog256DigestAdapter
{
    private readonly Gost3411_2012_256Digest _digest = new();

    public void AppendData(byte[] buffer, int count)
    {
        _digest.BlockUpdate(buffer, 0, count);
    }

    public byte[] GetHashAndReset()
    {
        var result = new byte[_digest.GetDigestSize()];
        _digest.DoFinal(result, 0);
        _digest.Reset();
        return result;
    }
}
