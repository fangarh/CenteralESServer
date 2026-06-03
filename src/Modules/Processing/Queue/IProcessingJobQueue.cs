namespace CenteralES.Processing.Queue;

public interface IProcessingJobCommandQueue
{
    Task<EnqueueProcessingJobResult> EnqueueAsync(CreateProcessingJobCommand command, CancellationToken cancellationToken);

    Task RegisterContentHashesAsync(RegisterProcessingContentHashesCommand command, CancellationToken cancellationToken);

    Task CompleteAsync(CompleteProcessingJobCommand command, CancellationToken cancellationToken);

    Task DeferAsync(DeferProcessingJobCommand command, CancellationToken cancellationToken);

    Task FailAsync(FailProcessingJobCommand command, CancellationToken cancellationToken);
}

public interface IProcessingJobClaimQueue
{
    Task<ClaimedProcessingJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task RefreshHeartbeatAsync(RefreshProcessingJobHeartbeatCommand command, CancellationToken cancellationToken);
}

public interface IProcessingJobRecoveryQueue
{
    Task<int> RecoverStaleProcessingJobsAsync(RecoverStaleProcessingJobsCommand command, CancellationToken cancellationToken);
}

public interface IProcessingJobReadStore
{
    Task<ProcessingJobSnapshot?> GetCurrentByHashAsync(string capability, string contentHash, CancellationToken cancellationToken);

    Task<ProcessingJobSnapshot?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);
}

public interface IProcessingJobQueue :
    IProcessingJobCommandQueue,
    IProcessingJobClaimQueue,
    IProcessingJobRecoveryQueue,
    IProcessingJobReadStore;
