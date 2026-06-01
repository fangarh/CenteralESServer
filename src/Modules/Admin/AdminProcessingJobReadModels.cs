using CenteralES.Processing;

namespace CenteralES.Admin;

public sealed record AdminProcessingJobListQuery(
    string? Capability,
    ProcessingJobStatus? Status,
    string? ContentHash,
    int Limit);

public sealed record AdminProcessingJobListItem(
    Guid JobId,
    Guid SubjectId,
    string Capability,
    string ContentHash,
    int AttemptNumber,
    ProcessingJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Endpoint,
    NormalizedProcessorError? NormalizedError,
    bool? Retryable,
    string? CorrelationId);

public sealed record AdminProcessingJobDetails(
    Guid JobId,
    Guid SubjectId,
    string Capability,
    string ContentHash,
    string TemporaryFileKey,
    int AttemptNumber,
    ProcessingJobStatus Status,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? HeartbeatAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Endpoint,
    TimeSpan? Duration,
    int? HttpStatus,
    NormalizedProcessorError? NormalizedError,
    bool? Retryable,
    string? RawErrorExcerpt,
    string? CorrelationId,
    IReadOnlyList<AdminProcessingAttemptDetails> Attempts);

public sealed record AdminProcessingAttemptDetails(
    Guid JobId,
    int AttemptNumber,
    ProcessingJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Endpoint,
    TimeSpan? Duration,
    int? HttpStatus,
    NormalizedProcessorError? NormalizedError,
    bool? Retryable,
    string? CorrelationId);

public sealed record AdminProcessorStatus(
    string ProcessorKey,
    string Capability,
    string Health,
    AdminProcessorQueueCounts Queue,
    IReadOnlyList<AdminProcessorWorkerStatus> Workers,
    IReadOnlyList<AdminProcessorRecentDiagnostic> RecentDiagnostics);

public sealed record AdminProcessorQueueCounts(
    int Queued,
    int Processing,
    int Completed,
    int Failed,
    int Blocked,
    int Cancelled);

public sealed record AdminProcessorRecentDiagnostic(
    Guid JobId,
    int AttemptNumber,
    ProcessingJobStatus Status,
    string? Endpoint,
    int? HttpStatus,
    NormalizedProcessorError? NormalizedError,
    bool? Retryable,
    string CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record AdminProcessorWorkerStatus(
    string WorkerId,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,
    bool Stale);

public interface IAdminProcessingReadStore
{
    Task<IReadOnlyList<AdminProcessingJobListItem>> ListJobsAsync(
        AdminProcessingJobListQuery query,
        CancellationToken cancellationToken);

    Task<AdminProcessingJobDetails?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<AdminProcessorStatus> GetProcessorStatusAsync(
        string processorKey,
        string capability,
        int recentDiagnosticsLimit,
        CancellationToken cancellationToken);
}
