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
        var endpointRuntimes = await ReadEndpointRuntimesAsync(connection, processorKey, capability, now, cancellationToken);
        var endpointDistribution = await ReadEndpointDistributionAsync(connection, capability, now, cancellationToken);
        var diagnostics = await ReadRecentDiagnosticsAsync(connection, capability, limit, cancellationToken);

        return new AdminProcessorStatus(
            processorKey,
            capability,
            Health: ToProcessorHealth(workers),
            queue,
            workers,
            endpointRuntimes,
            endpointDistribution,
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

    private static async Task<IReadOnlyList<AdminProcessorEndpointRuntime>> ReadEndpointRuntimesAsync(
        NpgsqlConnection connection,
        string processorKey,
        string capability,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var freshSince = now.Subtract(TimeSpan.FromMinutes(3));
        await using var command = new NpgsqlCommand("""
            select
                endpoint,
                count(*) filter (where heartbeat_at >= @fresh_since)::int as live_worker_count,
                count(*) filter (where heartbeat_at < @fresh_since)::int as stale_worker_count,
                coalesce(sum(in_flight) filter (where heartbeat_at >= @fresh_since), 0)::int as in_flight,
                coalesce(sum(concurrency_limit) filter (where heartbeat_at >= @fresh_since), 0)::int as concurrency_limit,
                case
                    when bool_or(health = 'unhealthy' and heartbeat_at >= @fresh_since) then 'unhealthy'
                    when bool_or(health = 'degraded' and heartbeat_at >= @fresh_since) then 'degraded'
                    when bool_or(health = 'healthy' and heartbeat_at >= @fresh_since) then 'healthy'
                    when bool_or(heartbeat_at >= @fresh_since) then 'unknown'
                    else 'unknown'
                end as health,
                max(heartbeat_at) as last_heartbeat_at
            from processing_worker_endpoint_metrics
            where processor_key = @processor_key
              and capability = @capability
            group by endpoint
            order by endpoint;
            """, connection);
        command.Parameters.AddWithValue("processor_key", processorKey);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("fresh_since", freshSince);

        var runtimes = new List<AdminProcessorEndpointRuntime>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runtimes.Add(new AdminProcessorEndpointRuntime(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return runtimes;
    }

    private static async Task<IReadOnlyList<AdminProcessorEndpointDistribution>> ReadEndpointDistributionAsync(
        NpgsqlConnection connection,
        string capability,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var throughputSince = now.Subtract(TimeSpan.FromMinutes(5));
        await using var command = new NpgsqlCommand("""
            with recent as (
                select
                    d.endpoint,
                    j.status,
                    d.duration_ms,
                    d.http_status,
                    d.normalized_error_code,
                    d.retryable,
                    d.created_at
                from processing_attempt_diagnostics d
                join processing_jobs j on j.id = d.job_id
                where j.capability = @capability
                  and d.endpoint is not null
                order by d.created_at desc
                limit 200
            ),
            ranked as (
                select
                    endpoint,
                    status,
                    duration_ms,
                    http_status,
                    normalized_error_code,
                    retryable,
                    created_at,
                    row_number() over (partition by endpoint order by created_at desc) as endpoint_rank
                from recent
            )
            select
                endpoint,
                count(*)::int as recent_attempts,
                count(*) filter (where status = 'completed')::int as completed,
                count(*) filter (where status = 'failed')::int as failed,
                count(*) filter (where status = 'blocked')::int as blocked,
                count(*) filter (where retryable is true)::int as retryable_failures,
                (avg(duration_ms) filter (where duration_ms is not null))::double precision as average_duration_ms,
                percentile_cont(0.95) within group (order by duration_ms) filter (where duration_ms is not null) as p95_duration_ms,
                (max(duration_ms) filter (where duration_ms is not null))::double precision as max_duration_ms,
                (max(duration_ms) filter (where endpoint_rank = 1))::double precision as last_duration_ms,
                (count(*) filter (
                    where status = 'completed'
                      and created_at >= @throughput_since)::double precision / 5.0) as completed_per_minute,
                max(http_status) filter (where endpoint_rank = 1) as last_http_status,
                max(normalized_error_code) filter (where endpoint_rank = 1) as last_normalized_error,
                max(created_at) as last_seen_at
            from ranked
            group by endpoint
            order by recent_attempts desc, endpoint
            limit 50;
            """, connection);
        command.Parameters.AddWithValue("capability", capability);
        command.Parameters.AddWithValue("throughput_since", throughputSince);

        var distribution = new List<AdminProcessorEndpointDistribution>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            distribution.Add(new AdminProcessorEndpointDistribution(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : Enum.Parse<NormalizedProcessorError>(reader.GetString(12)),
                reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13)));
        }

        return distribution;
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
