using CenteralES.Admin;
using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Processing;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using CenteralES.Processing.Workers;
using CenteralES.PdfStampRecognition;
using Npgsql;

namespace CenteralES.IntegrationTests;

public sealed class PostgresProcessingJobQueueTests
{
    [Fact]
    public async Task Enqueue_deduplicates_active_job_and_claims_it()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var queue = new PostgresProcessingJobQueue(dataSource);
        var command = new CreateProcessingJobCommand(
            "pdf-stamp-recognition",
            $"sha256:{Guid.NewGuid():N}",
            $"temp/{Guid.NewGuid():N}.pdf",
            DateTimeOffset.UtcNow);

        var first = await queue.EnqueueAsync(command, CancellationToken.None);
        var duplicate = await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);

        Assert.False(first.Deduplicated);
        Assert.True(duplicate.Deduplicated);
        Assert.Equal(first.JobId, duplicate.JobId);
        Assert.NotNull(claimed);
        Assert.Equal(first.JobId, claimed.JobId);
        Assert.Equal(1, claimed.AttemptNumber);
    }

    [Fact]
    public async Task Completed_job_can_store_and_read_pdf_result()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var queue = new PostgresProcessingJobQueue(dataSource);
        var resultStore = new PostgresPdfStampRecognitionResultStore(dataSource);
        var command = new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            $"temp/{Guid.NewGuid():N}.pdf",
            DateTimeOffset.UtcNow);

        var enqueued = await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(claimed);

        var saved = await resultStore.SaveAsync(
            new SavePdfStampRecognitionResultCommand(
                claimed.SubjectId,
                claimed.JobId,
                claimed.ContentHash,
                """{"source":"integration-test","people":[]}""",
                "test-v1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await queue.CompleteAsync(
            new CompleteProcessingJobCommand(
                claimed.JobId,
                claimed.SubjectId,
                saved.ResultIndexId,
                new AttemptDiagnostics(
                    Endpoint: "fake://test",
                    Duration: TimeSpan.FromMilliseconds(10),
                    HttpStatus: 200,
                    NormalizedError: null,
                    Retryable: null,
                    CorrelationId: Guid.NewGuid().ToString("N")),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var loaded = await resultStore.GetByHashAsync(command.ContentHash, CancellationToken.None);

        Assert.Equal(enqueued.JobId, claimed.JobId);
        Assert.NotNull(loaded);
        Assert.Equal(saved.ResultIndexId, loaded.ResultIndexId);
        Assert.Equal(command.ContentHash, loaded.ContentHash);
        Assert.Contains("integration-test", loaded.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Support_report_includes_result_index_reference_without_payload()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var queue = new PostgresProcessingJobQueue(dataSource);
        var resultStore = new PostgresPdfStampRecognitionResultStore(dataSource);
        var adminStore = new PostgresAdminProcessingReadStore(dataSource);
        var command = new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            $"temp/{Guid.NewGuid():N}.pdf",
            DateTimeOffset.UtcNow);

        var enqueued = await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(claimed);

        var saved = await resultStore.SaveAsync(
            new SavePdfStampRecognitionResultCommand(
                claimed.SubjectId,
                claimed.JobId,
                claimed.ContentHash,
                """{"source":"support-report-test","people":[{"name":"hidden payload"}]}""",
                "test-v1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await queue.CompleteAsync(
            new CompleteProcessingJobCommand(
                claimed.JobId,
                claimed.SubjectId,
                saved.ResultIndexId,
                new AttemptDiagnostics(
                    Endpoint: "fake://test",
                    Duration: TimeSpan.FromMilliseconds(10),
                    HttpStatus: 200,
                    NormalizedError: null,
                    Retryable: null,
                    CorrelationId: "corr-support-report"),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var report = await adminStore.GetJobSupportReportAsync(
            enqueued.JobId,
            PdfStampRecognitionConstants.ProcessorKey,
            CancellationToken.None);

        Assert.NotNull(report);
        Assert.NotNull(report.Result);
        Assert.Equal(saved.ResultIndexId, report.Result.ResultIndexId);
        Assert.Equal("json", report.Result.ResultKind);
        Assert.Equal("pdf_stamp_recognition_results", report.Result.PayloadTable);
        Assert.True(report.Result.PayloadSize > 0);
        Assert.Equal(PdfStampRecognitionConstants.ProcessorKey, report.ProcessorKey);
        Assert.Single(report.Attempts);
    }

    [Fact]
    public async Task Deferred_job_returns_to_queue_and_is_not_claimed_before_schedule()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var queue = new PostgresProcessingJobQueue(dataSource);
        var command = new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            $"temp/{Guid.NewGuid():N}.pdf",
            now);

        var enqueued = await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(now.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(claimed);

        await queue.DeferAsync(
            new DeferProcessingJobCommand(
                claimed.JobId,
                claimed.SubjectId,
                now.AddSeconds(30),
                now.AddSeconds(2)),
            CancellationToken.None);

        var earlyClaim = await queue.ClaimNextAsync(now.AddSeconds(10), CancellationToken.None);
        var laterClaim = await queue.ClaimNextAsync(now.AddSeconds(31), CancellationToken.None);

        Assert.Null(earlyClaim);
        Assert.NotNull(laterClaim);
        Assert.Equal(enqueued.JobId, laterClaim.JobId);
        Assert.Equal(claimed.AttemptNumber, laterClaim.AttemptNumber);
    }

    [Fact]
    public async Task Processing_job_heartbeat_can_be_refreshed()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var queue = new PostgresProcessingJobQueue(dataSource);
        var adminStore = new PostgresAdminProcessingReadStore(dataSource);
        var command = new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            $"temp/{Guid.NewGuid():N}.pdf",
            now);

        var enqueued = await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(now.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(claimed);

        var heartbeatAt = now.AddMinutes(2);
        await queue.RefreshHeartbeatAsync(
            new RefreshProcessingJobHeartbeatCommand(claimed.JobId, heartbeatAt),
            CancellationToken.None);

        var details = await adminStore.GetJobAsync(enqueued.JobId, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(ProcessingJobStatus.Processing, details.Status);
        Assert.NotNull(details.HeartbeatAt);
        Assert.Equal(heartbeatAt.ToUnixTimeMilliseconds(), details.HeartbeatAt.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Retryable_failure_schedules_next_attempt_for_same_temporary_file()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var queue = new PostgresProcessingJobQueue(dataSource);
        var command = new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            $"temp/{Guid.NewGuid():N}.pdf",
            now);

        var enqueued = await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(now.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(claimed);

        await queue.FailAsync(
            new FailProcessingJobCommand(
                claimed.JobId,
                claimed.SubjectId,
                NormalizedProcessorError.ProcessorTimeout,
                Final: false,
                new AttemptDiagnostics(
                    Endpoint: "https://pdf2txt.local/recognize_json/",
                    Duration: TimeSpan.FromSeconds(30),
                    HttpStatus: null,
                    NormalizedError: NormalizedProcessorError.ProcessorTimeout,
                    Retryable: true,
                    CorrelationId: "corr-retry",
                    RawErrorExcerpt: "Processor request timed out."),
                now.AddSeconds(2)),
            CancellationToken.None);

        var current = await queue.GetCurrentByHashAsync(PdfStampRecognitionConstants.Capability, command.ContentHash, CancellationToken.None);
        var earlyClaim = await queue.ClaimNextAsync(now.AddSeconds(10), CancellationToken.None);
        var retryClaim = await queue.ClaimNextAsync(now.AddSeconds(33), CancellationToken.None);

        Assert.NotNull(current);
        Assert.NotEqual(enqueued.JobId, current.JobId);
        Assert.Equal(2, current.AttemptNumber);
        Assert.Equal(ProcessingJobStatus.Queued, current.Status);
        Assert.Null(earlyClaim);
        Assert.NotNull(retryClaim);
        Assert.Equal(current.JobId, retryClaim.JobId);
        Assert.Equal(claimed.TemporaryFileKey, retryClaim.TemporaryFileKey);
        Assert.Equal(2, retryClaim.AttemptNumber);
    }

    [Fact]
    public async Task Manual_retry_creates_queued_attempt_and_audit_event_for_current_blocked_job()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var queue = new PostgresProcessingJobQueue(dataSource);
        var actions = new PostgresAdminProcessingActionStore(dataSource);
        var command = new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            $"sha256:{Guid.NewGuid():N}",
            $"temp/{Guid.NewGuid():N}.pdf",
            now);

        var enqueued = await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(now.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(claimed);

        await queue.FailAsync(
            new FailProcessingJobCommand(
                claimed.JobId,
                claimed.SubjectId,
                NormalizedProcessorError.ProcessorContractError,
                Final: true,
                new AttemptDiagnostics(
                    Endpoint: "https://pdf2txt.local/recognize_json/",
                    Duration: TimeSpan.FromMilliseconds(100),
                    HttpStatus: 200,
                    NormalizedError: NormalizedProcessorError.ProcessorContractError,
                    Retryable: true,
                    CorrelationId: "corr-blocked",
                    RawErrorExcerpt: "Unexpected response."),
                now.AddSeconds(2)),
            CancellationToken.None);

        var retry = await actions.ManualRetryJobAsync(
            new AdminManualRetryJobCommand(
                enqueued.JobId,
                Guid.NewGuid(),
                "admin",
                now.AddSeconds(3),
                "retry once",
                "127.0.0.1",
                "integration-test"),
            CancellationToken.None);

        var success = Assert.IsType<AdminManualRetryJobSuccess>(retry);
        var current = await queue.GetCurrentByHashAsync(PdfStampRecognitionConstants.Capability, command.ContentHash, CancellationToken.None);
        var retryClaim = await queue.ClaimNextAsync(now.AddSeconds(4), CancellationToken.None);
        var audit = await ReadAuditEventAsync(dataSource, success.AuditId, CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal(success.NewJobId, current.JobId);
        Assert.Equal(2, current.AttemptNumber);
        Assert.Equal(ProcessingJobStatus.Queued, current.Status);
        Assert.NotNull(retryClaim);
        Assert.Equal(success.NewJobId, retryClaim.JobId);
        Assert.Equal(claimed.TemporaryFileKey, retryClaim.TemporaryFileKey);
        Assert.Equal(AdminAuditActions.ManualRetryJob, audit.Action);
        Assert.Equal(enqueued.JobId.ToString("N"), audit.TargetId);
        Assert.Contains(success.NewJobId.ToString("N"), audit.NewValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_retry_rejects_non_current_or_active_job()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var queue = new PostgresProcessingJobQueue(dataSource);
        var actions = new PostgresAdminProcessingActionStore(dataSource);
        var enqueued = await queue.EnqueueAsync(
            new CreateProcessingJobCommand(
                PdfStampRecognitionConstants.Capability,
                $"sha256:{Guid.NewGuid():N}",
                $"temp/{Guid.NewGuid():N}.pdf",
                now),
            CancellationToken.None);

        var retry = await actions.ManualRetryJobAsync(
            new AdminManualRetryJobCommand(
                enqueued.JobId,
                Guid.NewGuid(),
                "admin",
                now.AddSeconds(1),
                null,
                null,
                null),
            CancellationToken.None);

        Assert.IsType<AdminManualRetryJobConflict>(retry);
    }

    [Fact]
    public async Task Worker_heartbeat_is_visible_in_admin_processor_status()
    {
        var connectionString = IntegrationTestDatabase.TryReadConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var bootstrapper = new PostgresDatabaseBootstrapper();

        await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
        await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

        var workerId = $"integration-worker-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var heartbeatStore = new PostgresWorkerHeartbeatStore(dataSource);
        var adminStore = new PostgresAdminProcessingReadStore(dataSource);

        await heartbeatStore.HeartbeatAsync(
            new HeartbeatWorkerCommand(
                workerId,
                PdfStampRecognitionConstants.ProcessorKey,
                PdfStampRecognitionConstants.Capability,
                now.AddMinutes(-1),
                now),
            CancellationToken.None);

        var status = await adminStore.GetProcessorStatusAsync(
            PdfStampRecognitionConstants.ProcessorKey,
            PdfStampRecognitionConstants.Capability,
            recentDiagnosticsLimit: 10,
            CancellationToken.None);

        Assert.Equal("healthy", status.Health);
        var worker = Assert.Single(status.Workers);
        Assert.Equal(workerId, worker.WorkerId);
        Assert.False(worker.Stale);
    }

    private static async Task ResetProcessingTablesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            truncate table
                admin_audit_events,
                processing_worker_heartbeats,
                pdf_stamp_recognition_results,
                processing_attempt_diagnostics,
                processing_result_index,
                processing_jobs,
                processing_subjects
            cascade;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AuditEventRow> ReadAuditEventAsync(
        NpgsqlDataSource dataSource,
        Guid auditId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select action, target_id, new_value_json::text
            from admin_audit_events
            where id = @id;
            """, connection);
        command.Parameters.AddWithValue("id", auditId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return new AuditEventRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private sealed record AuditEventRow(string Action, string TargetId, string NewValueJson);
}
