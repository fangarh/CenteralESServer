using CenteralES.Storage;

namespace CenteralES.UnitTests;

public sealed class LocalTemporaryStorageMonitorTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"centerales-storage-monitor-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CheckCapacity_reports_full_when_projected_bytes_exceed_hard_limit()
    {
        Directory.CreateDirectory(_rootPath);
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "existing.bin"), "12345"u8.ToArray());
        var monitor = new LocalTemporaryStorageMonitor(
            _rootPath,
            new TemporaryStorageLimits(HardLimitBytes: 7));

        var capacity = await monitor.CheckCapacityAsync(
            new TemporaryStorageCapacityRequest(IncomingBytes: 3),
            CancellationToken.None);

        Assert.Equal(TemporaryStorageCapacityStatus.Full, capacity.Status);
        Assert.Equal(5, capacity.UsedBytes);
        Assert.Equal(3, capacity.IncomingBytes);
        Assert.Equal(7, capacity.HardLimitBytes);
    }

    [Fact]
    public async Task CheckCapacity_reports_warning_when_projected_bytes_exceed_soft_limit_only()
    {
        Directory.CreateDirectory(_rootPath);
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "existing.bin"), "12345"u8.ToArray());
        var monitor = new LocalTemporaryStorageMonitor(
            _rootPath,
            new TemporaryStorageLimits(HardLimitBytes: 20, SoftLimitBytes: 7));

        var capacity = await monitor.CheckCapacityAsync(
            new TemporaryStorageCapacityRequest(IncomingBytes: 3),
            CancellationToken.None);

        Assert.Equal(TemporaryStorageCapacityStatus.Warning, capacity.Status);
    }

    [Fact]
    public async Task CheckCapacity_reports_healthy_when_no_limits_are_exceeded()
    {
        var monitor = new LocalTemporaryStorageMonitor(
            _rootPath,
            new TemporaryStorageLimits(HardLimitBytes: 20, SoftLimitBytes: 15));

        var capacity = await monitor.CheckCapacityAsync(
            new TemporaryStorageCapacityRequest(IncomingBytes: 3),
            CancellationToken.None);

        Assert.Equal(TemporaryStorageCapacityStatus.Healthy, capacity.Status);
        Assert.Equal(0, capacity.UsedBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
