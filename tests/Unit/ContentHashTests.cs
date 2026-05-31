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
}
