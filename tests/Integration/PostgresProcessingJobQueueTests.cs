using CenteralES.AccessControl;
using CenteralES.Admin;
using CenteralES.Infrastructure.AccessControl;
using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Processing;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using CenteralES.Processing.Workers;
using CenteralES.PdfStampRecognition;
using CenteralES.Storage;
using Npgsql;

namespace CenteralES.IntegrationTests;

public sealed class PostgresProcessingJobQueueTests
{
    [Fact]
    public async Task Apply_schema_records_baseline_migration_once()
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
        await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("""
            select count(*)
            from schema_migrations
            where id = '0001_processing_baseline';
            """, connection);

        var count = (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task Admin_bootstrap_creates_first_admin_once_and_writes_safe_audit()
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
        await ResetAdminBootstrapTablesAsync(dataSource, CancellationToken.None);

        var adminBootstrapper = new PostgresAdminBootstrapper(dataSource);
        const string password = "first-admin-password";

        var first = await adminBootstrapper.BootstrapFirstAdminAsync(
            new AdminBootstrapUserCommand(
                "bootstrap-admin",
                password,
                DateTimeOffset.UtcNow,
                "create first admin",
                "integration_test"),
            CancellationToken.None);

        var second = await adminBootstrapper.BootstrapFirstAdminAsync(
            new AdminBootstrapUserCommand(
                "another-admin",
                "another-password",
                DateTimeOffset.UtcNow,
                null,
                "integration_test"),
            CancellationToken.None);

        var success = Assert.IsType<AdminBootstrapUserSuccess>(first);
        var alreadyInitialized = Assert.IsType<AdminBootstrapAlreadyInitialized>(second);
        var stored = await ReadAdminBootstrapRowAsync(dataSource, success.User.UserId, CancellationToken.None);

        Assert.Equal("bootstrap-admin", success.User.Login);
        Assert.Equal(1, alreadyInitialized.ActiveAdminCount);
        Assert.NotEqual(password, stored.PasswordHash);
        Assert.Equal(AdminAuditActions.BootstrapAdminUser, stored.AuditAction);
        Assert.DoesNotContain(password, stored.AuditNewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain(password, stored.AuditTechnicalMetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_bootstrap_smoke_can_login_and_validate_session()
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
        await ResetAdminBootstrapTablesAsync(dataSource, CancellationToken.None);

        var adminBootstrapper = new PostgresAdminBootstrapper(dataSource);
        var authenticator = new PostgresAdminAuthenticator(dataSource);
        var now = DateTimeOffset.UtcNow;
        const string login = "bootstrap-smoke-admin";
        const string password = "bootstrap-smoke-password";

        var bootstrap = await adminBootstrapper.BootstrapFirstAdminAsync(
            new AdminBootstrapUserCommand(
                login,
                password,
                now,
                "bootstrap smoke",
                "integration_smoke"),
            CancellationToken.None);

        var loginOutcome = await authenticator.LoginAsync(
            new AdminLoginRequest(
                login,
                password,
                now.AddSeconds(1),
                "127.0.0.1",
                "integration-smoke"),
            CancellationToken.None);

        var validation = await authenticator.ValidateSessionAsync(
            new AdminSessionValidationRequest(
                loginOutcome.Credential?.SessionToken,
                loginOutcome.Credential?.CsrfToken,
                RequireCsrf: true,
                now.AddSeconds(2)),
            CancellationToken.None);

        var repeatedBootstrap = await adminBootstrapper.BootstrapFirstAdminAsync(
            new AdminBootstrapUserCommand(
                "second-bootstrap-admin",
                "second-bootstrap-password",
                now.AddSeconds(3),
                null,
                "integration_smoke"),
            CancellationToken.None);

        var success = Assert.IsType<AdminBootstrapUserSuccess>(bootstrap);
        var initialized = Assert.IsType<AdminBootstrapAlreadyInitialized>(repeatedBootstrap);

        Assert.Equal(AdminLoginStatus.Success, loginOutcome.Status);
        Assert.NotNull(loginOutcome.Credential);
        Assert.Equal(AdminSessionValidationStatus.Success, validation.Status);
        Assert.NotNull(validation.Principal);
        Assert.Equal(success.User.UserId, validation.Principal.UserId);
        Assert.Equal(login, validation.Principal.Login);
        Assert.Equal(1, initialized.ActiveAdminCount);
    }

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
    public async Task Queue_and_result_lookup_resolve_content_hash_aliases()
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
        var sha256Hash = $"sha256:{Guid.NewGuid():N}";
        var gostHash = $"gost-r-34.11-2012-256:{Guid.NewGuid():N}";
        var command = new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            sha256Hash,
            $"temp/{Guid.NewGuid():N}.pdf",
            DateTimeOffset.UtcNow,
            [
                new ProcessingContentHash("sha256", sha256Hash),
                new ProcessingContentHash("gost-r-34.11-2012-256", gostHash)
            ]);

        var enqueued = await queue.EnqueueAsync(command, CancellationToken.None);
        var activeByAlias = await queue.GetCurrentByHashAsync(
            PdfStampRecognitionConstants.Capability,
            gostHash,
            CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(activeByAlias);
        Assert.Equal(enqueued.JobId, activeByAlias.JobId);
        Assert.NotNull(claimed);

        var saved = await resultStore.SaveAsync(
            new SavePdfStampRecognitionResultCommand(
                claimed.SubjectId,
                claimed.JobId,
                claimed.ContentHash,
                """{"source":"alias-test"}""",
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

        var loadedByAlias = await resultStore.GetByHashAsync(gostHash, CancellationToken.None);

        Assert.NotNull(loadedByAlias);
        Assert.Equal(saved.ResultIndexId, loadedByAlias.ResultIndexId);
        Assert.Equal(sha256Hash, loadedByAlias.ContentHash);
        Assert.Contains("alias-test", loadedByAlias.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_store_replaces_previous_payload_for_same_hash()
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

        await queue.EnqueueAsync(command, CancellationToken.None);
        var claimed = await queue.ClaimNextAsync(DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(claimed);

        var first = await resultStore.SaveAsync(
            new SavePdfStampRecognitionResultCommand(
                claimed.SubjectId,
                claimed.JobId,
                claimed.ContentHash,
                """{"source":"first"}""",
                "test-v1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var second = await resultStore.SaveAsync(
            new SavePdfStampRecognitionResultCommand(
                claimed.SubjectId,
                claimed.JobId,
                claimed.ContentHash,
                """{"source":"second"}""",
                "test-v1",
                DateTimeOffset.UtcNow.AddSeconds(1)),
            CancellationToken.None);
        var loaded = await resultStore.GetByHashAsync(command.ContentHash, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var countPayloads = new NpgsqlCommand("""
            select count(*)::int
            from pdf_stamp_recognition_results
            where result_index_id = @result_index_id;
            """, connection);
        countPayloads.Parameters.AddWithValue("result_index_id", second.ResultIndexId);
        var payloadCount = Convert.ToInt32(await countPayloads.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(first.ResultIndexId, second.ResultIndexId);
        Assert.NotEqual(first.PayloadId, second.PayloadId);
        Assert.NotNull(loaded);
        Assert.Equal(second.PayloadId, loaded.PayloadId);
        Assert.Contains("second", loaded.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("first", loaded.PayloadJson, StringComparison.Ordinal);
        Assert.Equal(1, payloadCount);
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
        var adminStore = new PostgresAdminJobReadStore(dataSource, new PostgresAdminProcessorReadStore(dataSource));
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
    public async Task Admin_result_details_include_pdf_summary_without_raw_payload()
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
        var adminStore = new PostgresAdminResultReadStore(dataSource);
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
                """
                {
                  "errors": ["hidden raw processor detail should be excerpt only"],
                  "workers": [["Ivanov", "Engineer"], ["Petrov"]],
                  "workers_page": {"2": [["Ivanov"]], "15": [["Petrov"]]},
                  "unrecognized_pages": [3],
                  "izm_number": "42",
                  "people": [{"name":"hidden payload"}]
                }
                """,
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
                    CorrelationId: "corr-result-summary"),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var details = await adminStore.GetResultAsync(saved.ResultIndexId, CancellationToken.None);

        Assert.NotNull(details);
        Assert.NotNull(details.PdfStampRecognitionSummary);
        Assert.Equal(saved.ResultIndexId, details.Reference.ResultIndexId);
        Assert.Equal(2, details.PdfStampRecognitionSummary.WorkerGroupCount);
        Assert.Equal(3, details.PdfStampRecognitionSummary.WorkerTextItemCount);
        Assert.Equal(2, details.PdfStampRecognitionSummary.WorkerPageCount);
        Assert.Equal(1, details.PdfStampRecognitionSummary.UnrecognizedPageCount);
        Assert.Equal(1, details.PdfStampRecognitionSummary.ErrorCount);
        Assert.Equal("42", details.PdfStampRecognitionSummary.IzmNumber);
        Assert.Equal(["15", "2"], details.PdfStampRecognitionSummary.PageKeys);
        Assert.DoesNotContain("hidden payload", string.Join(" ", details.PdfStampRecognitionSummary.ErrorExcerpts), StringComparison.Ordinal);
        Assert.Equal(enqueued.JobId, details.Reference.JobId);
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
        var adminStore = new PostgresAdminJobReadStore(dataSource, new PostgresAdminProcessorReadStore(dataSource));
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
    public async Task Stale_processing_job_is_recovered_to_queue_and_claimed_again_without_new_attempt()
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

        var activeRecovery = await queue.RecoverStaleProcessingJobsAsync(
            new RecoverStaleProcessingJobsCommand(
                PdfStampRecognitionConstants.Capability,
                now,
                now.AddSeconds(2),
                Limit: 10),
            CancellationToken.None);
        var staleRecovery = await queue.RecoverStaleProcessingJobsAsync(
            new RecoverStaleProcessingJobsCommand(
                PdfStampRecognitionConstants.Capability,
                now.AddMinutes(10),
                now.AddMinutes(10),
                Limit: 10),
            CancellationToken.None);
        var recoveredClaim = await queue.ClaimNextAsync(now.AddMinutes(10).AddSeconds(1), CancellationToken.None);

        Assert.Equal(0, activeRecovery);
        Assert.Equal(1, staleRecovery);
        Assert.NotNull(recoveredClaim);
        Assert.Equal(enqueued.JobId, recoveredClaim.JobId);
        Assert.Equal(claimed.AttemptNumber, recoveredClaim.AttemptNumber);
        Assert.Equal(claimed.TemporaryFileKey, recoveredClaim.TemporaryFileKey);
    }

    [Fact]
    public async Task Complete_rejects_job_that_is_not_processing()
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
        var enqueued = await queue.EnqueueAsync(
            new CreateProcessingJobCommand(
                PdfStampRecognitionConstants.Capability,
                $"sha256:{Guid.NewGuid():N}",
                $"temp/{Guid.NewGuid():N}.pdf",
                now),
            CancellationToken.None);

        var claimed = await queue.ClaimNextAsync(now.AddSeconds(1), CancellationToken.None);
        Assert.NotNull(claimed);

        var complete = new CompleteProcessingJobCommand(
            claimed.JobId,
            claimed.SubjectId,
            Guid.NewGuid(),
            new AttemptDiagnostics(
                Endpoint: "https://pdf2txt.local/recognize_json/",
                Duration: TimeSpan.FromSeconds(1),
                HttpStatus: 200,
                NormalizedError: null,
                Retryable: null,
                CorrelationId: "corr-complete-once"),
            now.AddSeconds(2));

        await queue.CompleteAsync(complete, CancellationToken.None);

        var repeat = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.CompleteAsync(complete with { FinishedAt = now.AddSeconds(3) }, CancellationToken.None));
        Assert.Contains(enqueued.JobId.ToString(), repeat.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fail_rejects_queued_job_that_was_not_claimed()
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
        var enqueued = await queue.EnqueueAsync(
            new CreateProcessingJobCommand(
                PdfStampRecognitionConstants.Capability,
                $"sha256:{Guid.NewGuid():N}",
                $"temp/{Guid.NewGuid():N}.pdf",
                now),
            CancellationToken.None);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.FailAsync(
                new FailProcessingJobCommand(
                    enqueued.JobId,
                    enqueued.SubjectId,
                    NormalizedProcessorError.ProcessorTimeout,
                    Final: false,
                    new AttemptDiagnostics(
                        Endpoint: "https://pdf2txt.local/recognize_json/",
                        Duration: TimeSpan.FromSeconds(1),
                        HttpStatus: 504,
                        NormalizedError: NormalizedProcessorError.ProcessorTimeout,
                        Retryable: true,
                        CorrelationId: "corr-fail-queued",
                        RawErrorExcerpt: "timeout"),
                    now.AddSeconds(2)),
                CancellationToken.None));
        Assert.Contains(enqueued.JobId.ToString(), failure.Message, StringComparison.Ordinal);
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
    public async Task RegisterContentHashes_adds_aliases_to_existing_subject()
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
        var canonical = $"sha256:{Guid.NewGuid():N}";
        var alias = $"gost-r-34.11-2012-256:{Guid.NewGuid():N}";
        var enqueued = await queue.EnqueueAsync(
            new CreateProcessingJobCommand(
                PdfStampRecognitionConstants.Capability,
                canonical,
                $"temp/{Guid.NewGuid():N}.pdf",
                now),
            CancellationToken.None);

        await queue.RegisterContentHashesAsync(
            new RegisterProcessingContentHashesCommand(
                enqueued.SubjectId,
                PdfStampRecognitionConstants.Capability,
                now.AddSeconds(1),
                [new ProcessingContentHash(ContentHashAlgorithms.GostR34112012_256, alias)]),
            CancellationToken.None);

        var current = await queue.GetCurrentByHashAsync(
            PdfStampRecognitionConstants.Capability,
            alias,
            CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal(enqueued.JobId, current.JobId);
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
        var adminStore = new PostgresAdminProcessorReadStore(dataSource);

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

    private static async Task ResetAdminBootstrapTablesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            truncate table
                admin_audit_events,
                admin_sessions,
                admin_users
            cascade;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AdminBootstrapRow> ReadAdminBootstrapRowAsync(
        NpgsqlDataSource dataSource,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                users.password_hash,
                audit.action,
                audit.new_value_json::text,
                audit.technical_metadata_json::text
            from admin_users users
            join admin_audit_events audit
              on audit.target_id = replace(users.id::text, '-', '')
            where users.id = @user_id
              and audit.action = @action;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("action", AdminAuditActions.BootstrapAdminUser);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return new AdminBootstrapRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
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

    private sealed record AdminBootstrapRow(
        string PasswordHash,
        string AuditAction,
        string AuditNewValueJson,
        string AuditTechnicalMetadataJson);
}
