using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Processing;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
using CenteralES.PdfStampRecognition;
using Npgsql;

namespace CenteralES.IntegrationTests;

public sealed class PostgresProcessingJobQueueTests
{
    [Fact]
    public async Task Enqueue_deduplicates_active_job_and_claims_it()
    {
        var envPath = Path.Combine(GetRepositoryRoot(), "db.env");
        if (!File.Exists(envPath))
        {
            return;
        }

        var connectionString = PostgresConnectionString.ReadFromEnvFile(envPath, "test_db");
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
        var envPath = Path.Combine(GetRepositoryRoot(), "db.env");
        if (!File.Exists(envPath))
        {
            return;
        }

        var connectionString = PostgresConnectionString.ReadFromEnvFile(envPath, "test_db");
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

    private static async Task ResetProcessingTablesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            truncate table
                pdf_stamp_recognition_results,
                processing_attempt_diagnostics,
                processing_result_index,
                processing_jobs,
                processing_subjects
            cascade;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CenteralESServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
