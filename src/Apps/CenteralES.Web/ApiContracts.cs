using System.Text.Json;

internal sealed record HealthResponse(string Status, DateTimeOffset CheckedAt);

internal sealed record ReadyHealthResponse(
    string Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<HealthCheckItemResponse> Checks);

internal sealed record HealthCheckItemResponse(
    string Name,
    string Status);

internal sealed record PdfJobResponse(
    string Hash,
    string JobId,
    int AttemptNumber,
    string Status,
    bool Deduplicated);

internal sealed record PdfResultResponse(
    string Hash,
    string JobId,
    string Status,
    string ContractVersion,
    JsonElement Result);

internal sealed record ApiErrorResponse(ApiError Error)
{
    public static ApiErrorResponse Create(string code, string message)
    {
        return new ApiErrorResponse(new ApiError(code, message, null, Guid.NewGuid().ToString("N")));
    }
}

internal sealed record ApiError(string Code, string Message, object? Details, string CorrelationId);

internal readonly record struct ApiKeyCredential(string KeyId, string Secret);

internal sealed record AdminLoginRequestBody(string? Login, string? Password);

internal sealed record AdminLoginResponse(
    AdminUserResponse Admin,
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt);

internal sealed record AdminMeResponse(AdminUserResponse Admin);

internal sealed record AdminLogoutResponse(bool LoggedOut);

internal sealed record AdminUserResponse(
    string UserId,
    string Login,
    string Role);

internal sealed record AdminAuthorizationResult(IResult? Error, CenteralES.AccessControl.AdminPrincipal? Principal);

internal sealed record AdminManualRetryRequestBody(string? Comment);

internal sealed record AdminManualRetryResponse(
    string SourceJobId,
    string JobId,
    string Hash,
    int AttemptNumber,
    string Status,
    string AuditId);

internal sealed record AdminJobListResponse(IReadOnlyList<AdminJobListItemResponse> Jobs);

internal sealed record AdminJobListItemResponse(
    string JobId,
    string SubjectId,
    string Capability,
    string Hash,
    int AttemptNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Endpoint,
    string? NormalizedError,
    bool? Retryable,
    string? CorrelationId);

internal sealed record AdminJobDetailsResponse(
    string JobId,
    string SubjectId,
    string Capability,
    string Hash,
    string TemporaryFileKey,
    int AttemptNumber,
    string Status,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? HeartbeatAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AdminAttemptDiagnosticsResponse Diagnostics,
    IReadOnlyList<AdminProcessingAttemptResponse> Attempts);

internal sealed record AdminJobSupportReportResponse(
    DateTimeOffset GeneratedAt,
    string JobId,
    string SubjectId,
    string Capability,
    string ProcessorKey,
    string Hash,
    int AttemptNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? HeartbeatAt,
    AdminJobSupportReportDiagnosticsResponse Diagnostics,
    IReadOnlyList<AdminProcessingAttemptResponse> Attempts,
    AdminJobSupportReportResultReferenceResponse? Result,
    AdminProcessorStatusResponse Processor,
    IReadOnlyList<AdminJobSupportReportAuditEventResponse> AuditEvents);

internal sealed record AdminJobSupportReportDiagnosticsResponse(
    string? Endpoint,
    double? DurationMs,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string? CorrelationId,
    string? Excerpt);

internal sealed record AdminJobSupportReportResultReferenceResponse(
    string ResultIndexId,
    string ResultKind,
    string PayloadTable,
    string PayloadId,
    string ContractVersion,
    long PayloadSize,
    DateTimeOffset CreatedAt);

internal sealed record AdminJobSupportReportAuditEventResponse(
    string AuditId,
    DateTimeOffset OccurredAt,
    string? ActorLogin,
    string Action,
    string TargetType,
    string TargetId,
    string? Comment,
    string CorrelationId);

internal sealed record AdminAuditListResponse(IReadOnlyList<AdminAuditEventResponse> Events);

internal sealed record AdminAuditEventResponse(
    string AuditId,
    DateTimeOffset OccurredAt,
    string? ActorAdminId,
    string? ActorLogin,
    string Action,
    string TargetType,
    string TargetId,
    string? Comment,
    string CorrelationId);

internal sealed record AdminAttemptDiagnosticsResponse(
    string? Endpoint,
    double? DurationMs,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string? RawErrorExcerpt,
    string? CorrelationId);

internal sealed record AdminProcessingAttemptResponse(
    string JobId,
    int AttemptNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Endpoint,
    double? DurationMs,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string? CorrelationId);

internal sealed record AdminProcessorStatusResponse(
    string ProcessorKey,
    string Capability,
    string Health,
    AdminProcessorQueueCountsResponse Queue,
    IReadOnlyList<AdminProcessorWorkerStatusResponse> Workers,
    IReadOnlyList<AdminProcessorRecentDiagnosticResponse> RecentDiagnostics);

internal sealed record AdminProcessorQueueCountsResponse(
    int Queued,
    int Processing,
    int Completed,
    int Failed,
    int Blocked,
    int Cancelled);

internal sealed record AdminProcessorWorkerStatusResponse(
    string WorkerId,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,
    bool Stale);

internal sealed record AdminProcessorRecentDiagnosticResponse(
    string JobId,
    int AttemptNumber,
    string Status,
    string? Endpoint,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string CorrelationId,
    DateTimeOffset CreatedAt);
