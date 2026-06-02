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

public sealed record AdminJobSupportReport(
    DateTimeOffset GeneratedAt,
    Guid JobId,
    Guid SubjectId,
    string Capability,
    string ProcessorKey,
    string ContentHash,
    int AttemptNumber,
    ProcessingJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? HeartbeatAt,
    AdminJobSupportReportDiagnostics Diagnostics,
    IReadOnlyList<AdminProcessingAttemptDetails> Attempts,
    AdminJobSupportReportResultReference? Result,
    AdminProcessorStatus Processor,
    IReadOnlyList<AdminJobSupportReportAuditEvent> AuditEvents);

public sealed record AdminJobSupportReportDiagnostics(
    string? Endpoint,
    TimeSpan? Duration,
    int? HttpStatus,
    NormalizedProcessorError? NormalizedError,
    bool? Retryable,
    string? CorrelationId,
    string? Excerpt);

public sealed record AdminJobSupportReportResultReference(
    Guid ResultIndexId,
    string ResultKind,
    string PayloadTable,
    Guid PayloadId,
    string ContractVersion,
    long PayloadSize,
    DateTimeOffset CreatedAt);

public sealed record AdminJobSupportReportAuditEvent(
    Guid AuditId,
    DateTimeOffset OccurredAt,
    string? ActorLogin,
    string Action,
    string TargetType,
    string TargetId,
    string? Comment,
    string CorrelationId);

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

    Task<AdminJobSupportReport?> GetJobSupportReportAsync(
        Guid jobId,
        string processorKey,
        CancellationToken cancellationToken);

    Task<AdminProcessorStatus> GetProcessorStatusAsync(
        string processorKey,
        string capability,
        int recentDiagnosticsLimit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminAuditEventListItem>> ListAuditEventsAsync(
        AdminAuditEventListQuery query,
        CancellationToken cancellationToken);
}
