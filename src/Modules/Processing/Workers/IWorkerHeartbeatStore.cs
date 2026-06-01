namespace CenteralES.Processing.Workers;

public interface IWorkerHeartbeatStore
{
    Task HeartbeatAsync(HeartbeatWorkerCommand command, CancellationToken cancellationToken);
}
