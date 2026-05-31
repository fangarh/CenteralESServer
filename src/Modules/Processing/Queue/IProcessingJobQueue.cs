namespace CenteralES.Processing.Queue;

public interface IProcessingJobQueue
{
    Task<EnqueueProcessingJobResult> EnqueueAsync(CreateProcessingJobCommand command, CancellationToken cancellationToken);

    Task<ClaimedProcessingJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
