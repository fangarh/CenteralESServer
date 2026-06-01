using CenteralES.Processing;

namespace CenteralES.Admin;

public sealed record AdminManualRetryJobCommand(
    Guid SourceJobId,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminManualRetryJobResult(
    AdminManualRetryJobStatus Status,
    Guid? SourceJobId = null,
    Guid? NewJobId = null,
    string? ContentHash = null,
    int? AttemptNumber = null,
    ProcessingJobStatus? NewStatus = null,
    Guid? AuditId = null)
{
    public static AdminManualRetryJobResult Success(
        Guid sourceJobId,
        Guid newJobId,
        string contentHash,
        int attemptNumber,
        Guid auditId)
    {
        return new AdminManualRetryJobResult(
            AdminManualRetryJobStatus.Success,
            sourceJobId,
            newJobId,
            contentHash,
            attemptNumber,
            ProcessingJobStatus.Queued,
            auditId);
    }

    public static AdminManualRetryJobResult NotFound()
    {
        return new AdminManualRetryJobResult(AdminManualRetryJobStatus.NotFound);
    }

    public static AdminManualRetryJobResult Conflict(Guid sourceJobId)
    {
        return new AdminManualRetryJobResult(AdminManualRetryJobStatus.Conflict, sourceJobId);
    }
}

public enum AdminManualRetryJobStatus
{
    Success,
    NotFound,
    Conflict
}

public interface IAdminProcessingActionStore
{
    Task<AdminManualRetryJobResult> ManualRetryJobAsync(
        AdminManualRetryJobCommand command,
        CancellationToken cancellationToken);
}
