using System.Text;
using CenteralES.Storage;

namespace CenteralES.UnitTests;

public sealed class ContentHashTests
{
    [Fact]
    public async Task ComputeSha256Async_returns_prefixed_lowercase_hash()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("centeral"));

        var hash = await ContentHash.ComputeSha256Async(stream);

        Assert.Equal("sha256:42fd24a75f70ea8dd41a378e7b3b353ebc8382aed9ca5b2bb5e199dff4cddfdf", hash);
    }

    [Theory]
    [InlineData("", "gost-r-34.11-2012-256:3f539a213e97c802cc229d474c6aa32a825a360b2a933a949fd925208d9ce1bb")]
    [InlineData("012345678901234567890123456789012345678901234567890123456789012", "gost-r-34.11-2012-256:9d151eefd8590b89daa6ba6cb74af9275dd051026bb149a452fd84e5e57b5500")]
    public async Task ComputeGostR34112012_256Async_returns_prefixed_lowercase_hash(string content, string expected)
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var hash = await ContentHash.ComputeGostR34112012_256Async(stream);

        Assert.Equal(expected, hash);
    }

    [Fact]
    public async Task ComputeAllAsync_returns_all_supported_prefixed_hashes()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("centeral"));

        var hashes = await ContentHash.ComputeAllAsync(stream);

        Assert.Equal(2, hashes.Count);
        Assert.Contains(hashes, item =>
            item.Algorithm == ContentHashAlgorithm.Sha256
            && item.AlgorithmName == ContentHashAlgorithms.Sha256
            && item.Value == "sha256:42fd24a75f70ea8dd41a378e7b3b353ebc8382aed9ca5b2bb5e199dff4cddfdf");
        Assert.Contains(hashes, item =>
            item.Algorithm == ContentHashAlgorithm.GostR34112012_256
            && item.AlgorithmName == ContentHashAlgorithms.GostR34112012_256
            && item.Value.StartsWith("gost-r-34.11-2012-256:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sha256", ContentHashAlgorithm.Sha256)]
    [InlineData("Sha256", ContentHashAlgorithm.Sha256)]
    [InlineData("gost-r-34.11-2012-256", ContentHashAlgorithm.GostR34112012_256)]
    [InlineData("GostR34112012_256", ContentHashAlgorithm.GostR34112012_256)]
    [InlineData("streebog-256", ContentHashAlgorithm.GostR34112012_256)]
    public void TryParse_accepts_supported_wire_values(string? value, ContentHashAlgorithm expected)
    {
        var parsed = ContentHashAlgorithms.TryParse(value, out var algorithm);

        Assert.True(parsed);
        Assert.Equal(expected, algorithm);
    }

    [Fact]
    public void TryParse_rejects_unknown_wire_value()
    {
        var parsed = ContentHashAlgorithms.TryParse("md5", out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TryParse_rejects_missing_wire_value(string? value)
    {
        var parsed = ContentHashAlgorithms.TryParse(value, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void ToTemporaryStorageKeySegment_replaces_filesystem_unsafe_separators()
    {
        var key = ContentHash.ToTemporaryStorageKeySegment("gost-r-34.11-2012-256:abcdef");

        Assert.Equal("gost-r-34-11-2012-256-abcdef", key);
    }
}
