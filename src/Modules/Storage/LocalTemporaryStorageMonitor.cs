namespace CenteralES.Storage;

public sealed class LocalTemporaryStorageMonitor : ITemporaryStorageMonitor
{
    private readonly string _rootPath;
    private readonly TemporaryStorageLimits _limits;

    public LocalTemporaryStorageMonitor(string rootPath, TemporaryStorageLimits limits)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Temporary file root path is required.", nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
        _limits = limits;
        _limits.Validate();
    }

    public Task<TemporaryStorageCapacity> CheckCapacityAsync(
        TemporaryStorageCapacityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.IncomingBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Incoming bytes must not be negative.");
        }

        var usedBytes = Directory.Exists(_rootPath)
            ? EnumerateFileSizes(_rootPath, cancellationToken).Sum()
            : 0L;
        var availableFreeBytes = TryGetAvailableFreeBytes(_rootPath);
        var projectedBytes = usedBytes + request.IncomingBytes;

        var full = IsHardLimitExceeded(projectedBytes)
            || IsMinimumFreeBytesExceeded(availableFreeBytes, request.IncomingBytes);
        var warning = !full && IsSoftLimitExceeded(projectedBytes);

        return Task.FromResult(new TemporaryStorageCapacity(
            full
                ? TemporaryStorageCapacityStatus.Full
                : warning
                    ? TemporaryStorageCapacityStatus.Warning
                    : TemporaryStorageCapacityStatus.Healthy,
            usedBytes,
            request.IncomingBytes,
            _limits.HardLimitBytes,
            _limits.SoftLimitBytes,
            availableFreeBytes,
            _limits.MinimumFreeBytes));
    }

    private bool IsHardLimitExceeded(long projectedBytes)
    {
        return _limits.HardLimitBytes is not null && projectedBytes > _limits.HardLimitBytes;
    }

    private bool IsSoftLimitExceeded(long projectedBytes)
    {
        return _limits.SoftLimitBytes is not null && projectedBytes > _limits.SoftLimitBytes;
    }

    private bool IsMinimumFreeBytesExceeded(long? availableFreeBytes, long incomingBytes)
    {
        return _limits.MinimumFreeBytes is not null
            && availableFreeBytes is not null
            && availableFreeBytes - incomingBytes < _limits.MinimumFreeBytes;
    }

    private static IEnumerable<long> EnumerateFileSizes(string rootPath, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info;
            try
            {
                info = new FileInfo(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            yield return info.Length;
        }
    }

    private static long? TryGetAvailableFreeBytes(string rootPath)
    {
        try
        {
            var existingPath = Directory.Exists(rootPath)
                ? rootPath
                : FindExistingParent(rootPath);
            var root = Path.GetPathRoot(existingPath);
            return string.IsNullOrWhiteSpace(root)
                ? null
                : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string FindExistingParent(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null && !directory.Exists)
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? path;
    }
}
