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
            SanitizeOptionalEndpoint(job.Endpoint),
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
            !ProcessingInputRetentionPolicy.ShouldDeleteTemporaryInputAfterTerminalState(job.Status),
            job.AttemptNumber,
            ToPublicStatus(job.Status),
            job.ScheduledAt,
            job.StartedAt,
            job.FinishedAt,
            job.HeartbeatAt,
            job.CreatedAt,
            job.UpdatedAt,
            new AdminAttemptDiagnosticsResponse(
                SanitizeOptionalEndpoint(job.Endpoint),
                job.Duration?.TotalMilliseconds,
                job.HttpStatus,
                job.NormalizedError?.ToString(),
                job.Retryable,
                job.Excerpt,
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
                SanitizeOptionalEndpoint(report.Diagnostics.Endpoint),
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
            SanitizeOptionalEndpoint(attempt.Endpoint),
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

    public static AdminResultResponse ToAdminResultResponse(AdminResultReference result)
    {
        return new AdminResultResponse(
            result.ResultIndexId.ToString("N"),
            result.SubjectId.ToString("N"),
            result.JobId.ToString("N"),
            result.Capability,
            result.ContentHash,
            result.ResultKind,
            result.PayloadTable,
            result.PayloadId.ToString("N"),
            result.ContractVersion,
            result.PayloadSize,
            result.CreatedAt,
            result.JobStatus is null ? null : ToPublicStatus(result.JobStatus.Value),
            result.JobAttemptNumber);
    }

    public static AdminResultDetailsResponse ToAdminResultDetailsResponse(AdminResultDetails result)
    {
        return new AdminResultDetailsResponse(
            result.Reference.ResultIndexId.ToString("N"),
            result.Reference.SubjectId.ToString("N"),
            result.Reference.JobId.ToString("N"),
            result.Reference.Capability,
            result.Reference.ContentHash,
            result.Reference.ResultKind,
            result.Reference.PayloadTable,
            result.Reference.PayloadId.ToString("N"),
            result.Reference.ContractVersion,
            result.Reference.PayloadSize,
            result.Reference.CreatedAt,
            result.Reference.JobStatus is null ? null : ToPublicStatus(result.Reference.JobStatus.Value),
            result.Reference.JobAttemptNumber,
            result.PdfStampRecognitionSummary is null
                ? null
                : new AdminPdfStampRecognitionResultSummaryResponse(
                    result.PdfStampRecognitionSummary.WorkerGroupCount,
                    result.PdfStampRecognitionSummary.WorkerTextItemCount,
                    result.PdfStampRecognitionSummary.WorkerPageCount,
                    result.PdfStampRecognitionSummary.UnrecognizedPageCount,
                    result.PdfStampRecognitionSummary.ErrorCount,
                    result.PdfStampRecognitionSummary.IzmNumber,
                    result.PdfStampRecognitionSummary.PageKeys,
                    result.PdfStampRecognitionSummary.ErrorExcerpts));
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

    public static AdminProcessorStatusResponse ToAdminProcessorStatusResponse(
        AdminProcessorStatus status,
        IReadOnlyList<string>? configuredEndpoints = null)
    {
        return new AdminProcessorStatusResponse(
            status.ProcessorKey,
            status.Capability,
            status.Health,
            new AdminProcessorQueueCountsResponse(
                status.Queue.Queued,
                status.Queue.Processing,
                status.Queue.StaleProcessing,
                status.Queue.Completed,
                status.Queue.Failed,
                status.Queue.Blocked,
                status.Queue.Cancelled),
            status.Workers.Select(ToAdminProcessorWorkerStatusResponse).ToArray(),
            ToAdminProcessorEndpointDistributionResponses(
                status.EndpointDistribution,
                status.EndpointRuntimes,
                configuredEndpoints ?? Array.Empty<string>()),
            status.RecentDiagnostics.Select(ToAdminProcessorRecentDiagnosticResponse).ToArray());
    }

    private static IReadOnlyList<AdminProcessorEndpointDistributionResponse> ToAdminProcessorEndpointDistributionResponses(
        IReadOnlyList<AdminProcessorEndpointDistribution> observedEndpoints,
        IReadOnlyList<AdminProcessorEndpointRuntime> endpointRuntimes,
        IReadOnlyList<string> configuredEndpoints)
    {
        var configured = configuredEndpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var configuredSet = configured.ToHashSet(StringComparer.Ordinal);
        var byEndpoint = observedEndpoints
            .GroupBy(endpoint => AdminProcessorConfiguration.SanitizeEndpoint(endpoint.Endpoint), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var items = group.ToArray();
                    var latest = items
                        .Where(item => item.LastSeenAt is not null)
                        .OrderByDescending(item => item.LastSeenAt)
                        .FirstOrDefault();

                    return new AdminProcessorEndpointDistributionResponse(
                        group.Key,
                        configuredSet.Contains(group.Key),
                        items.Sum(item => item.RecentAttempts),
                        items.Sum(item => item.Completed),
                        items.Sum(item => item.Failed),
                        items.Sum(item => item.Blocked),
                        items.Sum(item => item.RetryableFailures),
                        ActiveProcessing: 0,
                        UtilizationPercent: 0,
                        AverageDurationMs: AverageOrNull(items.Select(item => item.AverageDurationMs)),
                        P95DurationMs: MaxOrNull(items.Select(item => item.P95DurationMs)),
                        MaxDurationMs: MaxOrNull(items.Select(item => item.MaxDurationMs)),
                        LastDurationMs: latest?.LastDurationMs,
                        CompletedPerMinute: items.Sum(item => item.CompletedPerMinute),
                        LiveWorkerCount: 0,
                        StaleWorkerCount: 0,
                        InFlight: 0,
                        ConcurrencyLimit: 0,
                        RuntimeHealth: "unknown",
                        LastHeartbeatAt: null,
                        latest?.LastHttpStatus,
                        latest?.LastNormalizedError?.ToString(),
                        latest?.LastSeenAt);
                },
                StringComparer.Ordinal);
        var runtimeByEndpoint = endpointRuntimes
            .GroupBy(endpoint => AdminProcessorConfiguration.SanitizeEndpoint(endpoint.Endpoint), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var items = group.ToArray();
                    var latest = items
                        .Where(item => item.LastHeartbeatAt is not null)
                        .OrderByDescending(item => item.LastHeartbeatAt)
                        .FirstOrDefault();

                    return new
                    {
                        LiveWorkerCount = items.Sum(item => item.LiveWorkerCount),
                        StaleWorkerCount = items.Sum(item => item.StaleWorkerCount),
                        InFlight = items.Sum(item => item.InFlight),
                        ConcurrencyLimit = items.Sum(item => item.ConcurrencyLimit),
                        Health = latest?.Health ?? "unknown",
                        LastHeartbeatAt = latest?.LastHeartbeatAt
                    };
                },
                StringComparer.Ordinal);
        var allEndpointKeys = configured
            .Concat(byEndpoint.Keys)
            .Concat(runtimeByEndpoint.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var endpoints = new List<AdminProcessorEndpointDistributionResponse>();
        foreach (var endpoint in allEndpointKeys)
        {
            byEndpoint.TryGetValue(endpoint, out var observed);
            runtimeByEndpoint.TryGetValue(endpoint, out var runtime);

            endpoints.Add(new AdminProcessorEndpointDistributionResponse(
                endpoint,
                Configured: configuredSet.Contains(endpoint),
                observed?.RecentAttempts ?? 0,
                observed?.Completed ?? 0,
                observed?.Failed ?? 0,
                observed?.Blocked ?? 0,
                observed?.RetryableFailures ?? 0,
                ActiveProcessing: runtime?.InFlight ?? 0,
                UtilizationPercent: CalculateUtilizationPercent(runtime?.InFlight ?? 0, runtime?.ConcurrencyLimit ?? 0),
                observed?.AverageDurationMs,
                observed?.P95DurationMs,
                observed?.MaxDurationMs,
                observed?.LastDurationMs,
                observed?.CompletedPerMinute ?? 0,
                runtime?.LiveWorkerCount ?? 0,
                runtime?.StaleWorkerCount ?? 0,
                runtime?.InFlight ?? 0,
                runtime?.ConcurrencyLimit ?? 0,
                runtime?.Health ?? "unknown",
                runtime?.LastHeartbeatAt,
                observed?.LastHttpStatus,
                observed?.LastNormalizedError,
                observed?.LastSeenAt));
        }

        return endpoints
            .OrderBy(endpoint => endpoint.Configured ? 0 : 1)
            .ThenByDescending(endpoint => endpoint.LiveWorkerCount)
            .ThenByDescending(endpoint => endpoint.RecentAttempts)
            .ThenBy(endpoint => endpoint.Endpoint, StringComparer.Ordinal)
            .ToArray();
    }

    private static int CalculateUtilizationPercent(int inFlight, int concurrencyLimit)
    {
        if (inFlight <= 0 || concurrencyLimit <= 0)
        {
            return 0;
        }

        return (int)Math.Round(inFlight * 100d / concurrencyLimit, MidpointRounding.AwayFromZero);
    }

    private static double? AverageOrNull(IEnumerable<double?> values)
    {
        var materialized = values
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();

        return materialized.Length == 0 ? null : materialized.Average();
    }

    private static double? MaxOrNull(IEnumerable<double?> values)
    {
        var materialized = values
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();

        return materialized.Length == 0 ? null : materialized.Max();
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
            SanitizeOptionalEndpoint(diagnostic.Endpoint),
            diagnostic.HttpStatus,
            diagnostic.NormalizedError?.ToString(),
            diagnostic.Retryable,
            diagnostic.CorrelationId,
            diagnostic.CreatedAt);
    }

    public static AdminProcessorEndpointResponse ToAdminProcessorEndpointResponse(AdminProcessorEndpointListItem endpoint)
    {
        return new AdminProcessorEndpointResponse(
            endpoint.Id?.ToString("N"),
            endpoint.ProcessorKey,
            endpoint.Capability,
            AdminProcessorConfiguration.SanitizeEndpoint(endpoint.Endpoint),
            endpoint.Enabled,
            endpoint.ConcurrencyLimit,
            endpoint.Priority,
            endpoint.Source,
            endpoint.CreatedAt,
            endpoint.UpdatedAt,
            endpoint.DisabledAt);
    }

    public static AdminProcessorEffectiveEndpointResponse ToAdminProcessorEffectiveEndpointResponse(
        ProcessorEndpointConfiguration endpoint)
    {
        return new AdminProcessorEffectiveEndpointResponse(
            AdminProcessorConfiguration.SanitizeEndpoint(endpoint.Endpoint),
            endpoint.ConcurrencyLimit,
            endpoint.Source);
    }

    public static AdminProcessorEndpointCheckResponse ToAdminProcessorEndpointCheckResponse(
        PdfStampRecognitionEndpointCheckResult check,
        DateTimeOffset? nextAllowedAt)
    {
        return new AdminProcessorEndpointCheckResponse(
            check.Status switch
            {
                PdfStampRecognitionEndpointCheckStatus.Succeeded => "succeeded",
                PdfStampRecognitionEndpointCheckStatus.Failed => "failed",
                PdfStampRecognitionEndpointCheckStatus.NotConfigured => "notConfigured",
                _ => "unknown"
            },
            check.Endpoint,
            check.CheckedAt,
            check.Duration?.TotalMilliseconds,
            check.HttpStatus,
            check.NormalizedError?.ToString(),
            check.Retryable,
            check.ResponseSizeBytes,
            check.RawResponseExcerpt,
            nextAllowedAt);
    }

    private static string? SanitizeOptionalEndpoint(string? endpoint)
    {
        return string.IsNullOrWhiteSpace(endpoint)
            ? endpoint
            : AdminProcessorConfiguration.SanitizeEndpoint(endpoint);
    }

    public static AdminUserResponse ToAdminUserResponse(AdminPrincipal principal)
    {
        return new AdminUserResponse(
            principal.UserId.ToString("N"),
            principal.Login,
            principal.Role);
    }

    public static AdminManagedUserResponse ToAdminManagedUserResponse(AdminUserListItem user)
    {
        return new AdminManagedUserResponse(
            user.UserId.ToString("N"),
            user.Login,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastLoginAt,
            user.DisabledAt);
    }

    public static AdminCreateUserResponse ToAdminCreateUserResponse(AdminCreateUserSuccess success)
    {
        return new AdminCreateUserResponse(
            success.User.UserId.ToString("N"),
            success.User.Login,
            success.User.Role,
            success.User.IsActive,
            success.User.CreatedAt,
            success.AuditId.ToString("N"));
    }

    public static AdminDisableUserResponse ToAdminDisableUserResponse(AdminDisableUserSuccess success)
    {
        return new AdminDisableUserResponse(
            success.User.UserId.ToString("N"),
            success.User.Login,
            success.User.Role,
            success.User.IsActive,
            success.User.DisabledAt,
            success.AuditId.ToString("N"));
    }

    public static AdminChangeUserPasswordResponse ToAdminChangeUserPasswordResponse(AdminChangeUserPasswordSuccess success)
    {
        return new AdminChangeUserPasswordResponse(
            success.User.UserId.ToString("N"),
            success.User.Login,
            success.AuditId.ToString("N"));
    }
}
