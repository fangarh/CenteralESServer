using System.Text;
using CenteralES.Storage;

namespace CenteralES.UnitTests;

public sealed class LocalTemporaryFileStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"centerales-storage-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Save_open_and_delete_roundtrip_uses_relative_key_under_root()
    {
        var store = new LocalTemporaryFileStore(_rootPath);
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("pdf bytes"));

        await store.SaveAsync("incoming/test.pdf", input, CancellationToken.None);

        string content;
        await using (var output = await store.OpenReadAsync("incoming/test.pdf", CancellationToken.None))
        using (var reader = new StreamReader(output, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024))
        {
            content = await reader.ReadToEndAsync(CancellationToken.None);
        }

        Assert.Equal("pdf bytes", content);

        await store.DeleteIfExistsAsync("incoming/test.pdf", CancellationToken.None);

        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync("incoming/test.pdf", CancellationToken.None));
    }

    [Fact]
    public async Task Save_open_and_delete_roundtrip_cleans_health_probe_key()
    {
        var store = new LocalTemporaryFileStore(_rootPath);
        var key = $".health/ready-{Guid.NewGuid():N}.probe";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("ok"));

        await store.SaveAsync(key, input, CancellationToken.None);
        await using (var output = await store.OpenReadAsync(key, CancellationToken.None))
        {
            Assert.Equal((byte)'o', output.ReadByte());
        }

        await store.DeleteIfExistsAsync(key, CancellationToken.None);

        var healthDirectory = Path.Combine(_rootPath, ".health");
        var leftovers = Directory.Exists(healthDirectory)
            ? Directory.EnumerateFiles(healthDirectory).ToArray()
            : Array.Empty<string>();
        Assert.Empty(leftovers);
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("incoming/../../outside.pdf")]
    [InlineData("/absolute.pdf")]
    public async Task Save_rejects_keys_that_escape_root(string key)
    {
        var store = new LocalTemporaryFileStore(_rootPath);
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("pdf bytes"));

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(key, input, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
