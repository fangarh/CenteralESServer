using CenteralES.Admin;
using CenteralES.Processing;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresAdminProcessorReadStore : IAdminProcessorReadStore
{
    private static readonly TimeSpan StaleProcessingJobTimeout = TimeSpan.FromMinutes(5);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminProcessorReadStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<AdminProcessorStatus> GetProcessorStatusAsync(
        string processorKey,
        string capability,
        int recentDiagnosticsLimit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        var limit = Math.Clamp(recentDiagnosticsLimit, 1, 50);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var queue = await ReadQueueCountsAsync(connection, capability, now.Subtract(StaleProcessingJobTimeout), cancellationToken);
        var workers = await ReadWorkersAsync(connection, processorKey, capability, now, cancellationToken);
        var diagnostics = await ReadRecentDiagnosticsAsync(connection, capability, limit, cancellationToken);

        return new AdminProcessorStatus(
            processorKey,
            capability,
            Health: ToProcessorHealth(workers),
            queue,
            workers,
            diagnostics);
    }

    private static string ToProcessorHealth(IReadOnlyList<AdminProcessorWorkerStatus> workers)
    {
        if (workers.Count == 0)
        {
            return "unknown";
        }

        return workers.Any(worker => !worker.Stale) ? "healthy" : "unhealthy";
    }

    private static async Task<IReadOnlyList<AdminProcessorWorkerStatus>> ReadWorkersAsync(
        NpgsqlConnection connection,
        string processorKey,
        string capability,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                worker_id,
                started_at,
                heartbeat_at
            from processing_worker_heartbeats
            where processor_key = @processor_key
              and capability = @capability
            order by heartbeat_at desc;
            """, connection);
        command.Parameters.AddWithValue("processor_key", processorKey);
        command.Parameters.AddWithValue("capability", capability);

        var workers = new List<AdminProcessorWorkerStatus>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var heartbeatAt = reader.GetFieldValue<DateTimeOffset>(2);
            workers.Add(new AdminProcessorWorkerStatus(
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                heartbeatAt,
                Stale: now - heartbeatAt > TimeSpan.FromMinutes(3)));
        }

        return workers;
    }

    private static async Task<AdminProcessorQueueCounts> ReadQueueCountsAsync(
        NpgsqlConnection connection,
        string capability,
        DateTimeOffset staleProcessingBefore,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                count(*) filter (where status = 'queued')::int,
                count(*) filter (where status = 'processing')::int,
                count(*) filter (
                    where status = 'processing'
                      and coalesce(heartbeat_at, started_at, updated_at) < @stale_processing_before)::int,
                count(*) filter (where status = 'completed')::int,
                count(*) filter (where status = 'failed')::int,
                count(*) filter (where status = 'blocked')::int,
                count(*) filter (where status = 'cancelled')::int
            from processing_jobs
            where capability = @capability;
            """, connection);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("stale_processing_before", staleProcessingBefore);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Processor queue count query returned no rows.");
        }

        return new AdminProcessorQueueCounts(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6));
    }

    private static async Task<IReadOnlyList<AdminProcessorRecentDiagnostic>> ReadRecentDiagnosticsAsync(
        NpgsqlConnection connection,
        string capability,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                j.id,
                j.attempt_number,
                j.status,
                d.endpoint,
                d.http_status,
                d.normalized_error_code,
                d.retryable,
                d.correlation_id,
                d.created_at
            from processing_attempt_diagnostics d
            join processing_jobs j on j.id = d.job_id
            where j.capability = @capability
            order by d.created_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("limit", limit);

        var diagnostics = new List<AdminProcessorRecentDiagnostic>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            diagnostics.Add(new AdminProcessorRecentDiagnostic(
                reader.GetGuid(0),
                reader.GetInt32(1),
                ProcessingJobStatusMapper.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return diagnostics;
    }
}
