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

public abstract record AdminManualRetryJobResult;

public sealed record AdminManualRetryJobSuccess(
    Guid SourceJobId,
    Guid NewJobId,
    string ContentHash,
    int AttemptNumber,
    ProcessingJobStatus NewStatus,
    Guid AuditId) : AdminManualRetryJobResult;

public sealed record AdminManualRetryJobNotFound : AdminManualRetryJobResult;

public sealed record AdminManualRetryJobConflict(Guid SourceJobId) : AdminManualRetryJobResult;

public interface IAdminProcessingActionStore
{
    Task<AdminManualRetryJobResult> ManualRetryJobAsync(
        AdminManualRetryJobCommand command,
        CancellationToken cancellationToken);
}
