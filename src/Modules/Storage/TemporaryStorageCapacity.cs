namespace CenteralES.Storage;

public sealed record TemporaryStorageCapacityRequest(long IncomingBytes);

public sealed record TemporaryStorageCapacity(
    TemporaryStorageCapacityStatus Status,
    long UsedBytes,
    long IncomingBytes,
    long? HardLimitBytes,
    long? SoftLimitBytes,
    long? AvailableFreeBytes,
    long? MinimumFreeBytes);

public enum TemporaryStorageCapacityStatus
{
    Healthy,
    Warning,
    Full
}

public sealed record TemporaryStorageLimits(
    long? HardLimitBytes = null,
    long? SoftLimitBytes = null,
    long? MinimumFreeBytes = null)
{
    public void Validate()
    {
        if (HardLimitBytes is <= 0)
        {
            throw new InvalidOperationException("Storage:TemporaryHardLimitBytes must be a positive integer when configured.");
        }

        if (SoftLimitBytes is <= 0)
        {
            throw new InvalidOperationException("Storage:TemporarySoftLimitBytes must be a positive integer when configured.");
        }

        if (MinimumFreeBytes is <= 0)
        {
            throw new InvalidOperationException("Storage:TemporaryMinimumFreeBytes must be a positive integer when configured.");
        }

        if (SoftLimitBytes is not null
            && HardLimitBytes is not null
            && SoftLimitBytes >= HardLimitBytes)
        {
            throw new InvalidOperationException("Storage:TemporarySoftLimitBytes must be less than Storage:TemporaryHardLimitBytes.");
        }
    }
}

public interface ITemporaryStorageMonitor
{
    Task<TemporaryStorageCapacity> CheckCapacityAsync(
        TemporaryStorageCapacityRequest request,
        CancellationToken cancellationToken);
}
