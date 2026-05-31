using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.Processing;
using CenteralES.Processing;
using CenteralES.Processing.Queue;
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

    private static async Task ResetProcessingTablesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            truncate table
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
