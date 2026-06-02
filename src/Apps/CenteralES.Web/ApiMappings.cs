using System.Text.Json;
using CenteralES.AccessControl;
using CenteralES.Admin;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing;
using CenteralES.Processing.Queue;

internal static class ApiMappings
{
    public static string ToPublicStatus(ProcessingJobStatus status)
    {
        return status.ToDatabaseValue();
    }

    public static PdfResultResponse ToPdfResultResponse(PdfStampRecognitionResult result)
    {
        using var payload = JsonDocument.Parse(result.PayloadJson);

        return new PdfResultResponse(
            result.ContentHash,
            result.JobId.ToString("N"),
            "completed",
            result.ContractVersion,
            payload.RootElement.Clone());
    }

    public static PdfJobResponse ToPdfJobResponse(ProcessingJobSnapshot job, bool deduplicated)
    {
        return new PdfJobResponse(
            job.ContentHash,
            job.JobId.ToString("N"),
            job.AttemptNumber,
            ToPublicStatus(job.Status),
            deduplicated);
    }

    public static bool IsPublicPending(ProcessingJobStatus status)
    {
        return status is ProcessingJobStatus.Queued or ProcessingJobStatus.Processing;
    }

    public static bool TryParseOptionalStatus(string? status, out ProcessingJobStatus? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        return ProcessingJobStatusMapper.TryParse(status, out parsed);
    }

    public static AdminJobListItemResponse ToAdminJobListItemResponse(AdminProcessingJobListItem job)
    {
        return new AdminJobListItemResponse(
            job.JobId.ToString("N"),
            job.SubjectId.ToString("N"),
            job.Capability,
            job.ContentHash,
            job.AttemptNumber,
            ToPublicStatus(job.Status),
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            job.Endpoint,
            job.NormalizedError?.ToString(),
            job.Retryable,
            job.CorrelationId);
    }

    public static AdminJobDetailsResponse ToAdminJobDetailsResponse(AdminProcessingJobDetails job)
    {
        return new AdminJobDetailsResponse(
            job.JobId.ToString("N"),
            job.SubjectId.ToString("N"),
            job.Capability,
            job.ContentHash,
            job.TemporaryFileKey,
            job.AttemptNumber,
            ToPublicStatus(job.Status),
            job.ScheduledAt,
            job.StartedAt,
            job.FinishedAt,
            job.HeartbeatAt,
            job.CreatedAt,
            job.UpdatedAt,
            new AdminAttemptDiagnosticsResponse(
                job.Endpoint,
                job.Duration?.TotalMilliseconds,
                job.HttpStatus,
                job.NormalizedError?.ToString(),
                job.Retryable,
                job.RawErrorExcerpt,
                job.CorrelationId),
            job.Attempts.Select(ToAdminProcessingAttemptResponse).ToArray());
    }

    public static AdminJobSupportReportResponse ToAdminJobSupportReportResponse(AdminJobSupportReport report)
    {
        return new AdminJobSupportReportResponse(
            report.GeneratedAt,
            report.JobId.ToString("N"),
            report.SubjectId.ToString("N"),
            report.Capability,
            report.ProcessorKey,
            report.ContentHash,
            report.AttemptNumber,
            ToPublicStatus(report.Status),
            report.CreatedAt,
            report.StartedAt,
            report.FinishedAt,
            report.HeartbeatAt,
            new AdminJobSupportReportDiagnosticsResponse(
                report.Diagnostics.Endpoint,
                report.Diagnostics.Duration?.TotalMilliseconds,
                report.Diagnostics.HttpStatus,
                report.Diagnostics.NormalizedError?.ToString(),
                report.Diagnostics.Retryable,
                report.Diagnostics.CorrelationId,
                report.Diagnostics.Excerpt),
            report.Attempts.Select(ToAdminProcessingAttemptResponse).ToArray(),
            report.Result is null
                ? null
                : new AdminJobSupportReportResultReferenceResponse(
                    report.Result.ResultIndexId.ToString("N"),
                    report.Result.ResultKind,
                    report.Result.PayloadTable,
                    report.Result.PayloadId.ToString("N"),
                    report.Result.ContractVersion,
                    report.Result.PayloadSize,
                    report.Result.CreatedAt),
            ToAdminProcessorStatusResponse(report.Processor),
            report.AuditEvents.Select(ToAdminJobSupportReportAuditEventResponse).ToArray());
    }

    public static AdminProcessingAttemptResponse ToAdminProcessingAttemptResponse(AdminProcessingAttemptDetails attempt)
    {
        return new AdminProcessingAttemptResponse(
            attempt.JobId.ToString("N"),
            attempt.AttemptNumber,
            ToPublicStatus(attempt.Status),
            attempt.CreatedAt,
            attempt.StartedAt,
            attempt.FinishedAt,
            attempt.Endpoint,
            attempt.Duration?.TotalMilliseconds,
            attempt.HttpStatus,
            attempt.NormalizedError?.ToString(),
            attempt.Retryable,
            attempt.CorrelationId);
    }

    public static AdminJobSupportReportAuditEventResponse ToAdminJobSupportReportAuditEventResponse(
        AdminJobSupportReportAuditEvent audit)
    {
        return new AdminJobSupportReportAuditEventResponse(
            audit.AuditId.ToString("N"),
            audit.OccurredAt,
            audit.ActorLogin,
            audit.Action,
            audit.TargetType,
            audit.TargetId,
            audit.Comment,
            audit.CorrelationId);
    }

    public static AdminAuditEventResponse ToAdminAuditEventResponse(AdminAuditEventListItem audit)
    {
        return new AdminAuditEventResponse(
            audit.AuditId.ToString("N"),
            audit.OccurredAt,
            audit.ActorAdminId?.ToString("N"),
            audit.ActorLogin,
            audit.Action,
            audit.TargetType,
            audit.TargetId,
            audit.Comment,
            audit.CorrelationId);
    }

    public static AdminApiKeyResponse ToAdminApiKeyResponse(AdminApiKeyListItem key)
    {
        return new AdminApiKeyResponse(
            key.KeyId,
            key.Name,
            key.IsActive,
            key.AllowedCapabilities,
            key.CreatedAt,
            key.UpdatedAt,
            key.ExpiresAt,
            key.LastUsedAt,
            key.DisabledAt);
    }

    public static AdminCreateApiKeyResponse ToAdminCreateApiKeyResponse(AdminCreateApiKeySuccess success)
    {
        return new AdminCreateApiKeyResponse(
            success.Key.KeyId,
            success.Key.Name,
            success.Key.IsActive,
            success.Key.AllowedCapabilities,
            success.Key.CreatedAt,
            success.Key.UpdatedAt,
            success.Key.ExpiresAt,
            success.Secret,
            success.AuditId.ToString("N"));
    }

    public static AdminDisableApiKeyResponse ToAdminDisableApiKeyResponse(AdminDisableApiKeySuccess success)
    {
        return new AdminDisableApiKeyResponse(
            success.Key.KeyId,
            success.Key.Name,
            success.Key.IsActive,
            success.Key.DisabledAt,
            success.AuditId.ToString("N"));
    }

    public static AdminProcessorStatusResponse ToAdminProcessorStatusResponse(AdminProcessorStatus status)
    {
        return new AdminProcessorStatusResponse(
            status.ProcessorKey,
            status.Capability,
            status.Health,
            new AdminProcessorQueueCountsResponse(
                status.Queue.Queued,
                status.Queue.Processing,
                status.Queue.Completed,
                status.Queue.Failed,
                status.Queue.Blocked,
                status.Queue.Cancelled),
            status.Workers.Select(ToAdminProcessorWorkerStatusResponse).ToArray(),
            status.RecentDiagnostics.Select(ToAdminProcessorRecentDiagnosticResponse).ToArray());
    }

    public static AdminProcessorWorkerStatusResponse ToAdminProcessorWorkerStatusResponse(AdminProcessorWorkerStatus worker)
    {
        return new AdminProcessorWorkerStatusResponse(
            worker.WorkerId,
            worker.StartedAt,
            worker.HeartbeatAt,
            worker.Stale);
    }

    public static AdminProcessorRecentDiagnosticResponse ToAdminProcessorRecentDiagnosticResponse(AdminProcessorRecentDiagnostic diagnostic)
    {
        return new AdminProcessorRecentDiagnosticResponse(
            diagnostic.JobId.ToString("N"),
            diagnostic.AttemptNumber,
            ToPublicStatus(diagnostic.Status),
            diagnostic.Endpoint,
            diagnostic.HttpStatus,
            diagnostic.NormalizedError?.ToString(),
            diagnostic.Retryable,
            diagnostic.CorrelationId,
            diagnostic.CreatedAt);
    }

    public static AdminUserResponse ToAdminUserResponse(AdminPrincipal principal)
    {
        return new AdminUserResponse(
            principal.UserId.ToString("N"),
            principal.Login,
            principal.Role);
    }
}
