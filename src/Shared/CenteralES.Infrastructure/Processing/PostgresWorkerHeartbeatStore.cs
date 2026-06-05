using CenteralES.Processing.Workers;
using Npgsql;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresWorkerHeartbeatStore : IWorkerHeartbeatStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresWorkerHeartbeatStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task HeartbeatAsync(HeartbeatWorkerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var upsert = new NpgsqlCommand("""
            insert into processing_worker_heartbeats (
                worker_id,
                processor_key,
                capability,
                started_at,
                heartbeat_at,
                updated_at)
            values (
                @worker_id,
                @processor_key,
                @capability,
                @started_at,
                @heartbeat_at,
                @heartbeat_at)
            on conflict (worker_id) do update
            set processor_key = excluded.processor_key,
                capability = excluded.capability,
                started_at = excluded.started_at,
                heartbeat_at = excluded.heartbeat_at,
                updated_at = excluded.updated_at;
            """, connection, transaction);
        upsert.Parameters.AddWithValue("worker_id", command.WorkerId);
        upsert.Parameters.AddWithValue("processor_key", command.ProcessorKey);
        upsert.Parameters.AddWithValue("capability", command.Capability);
        upsert.Parameters.AddWithValue("started_at", command.StartedAt);
        upsert.Parameters.AddWithValue("heartbeat_at", command.HeartbeatAt);

        await upsert.ExecuteNonQueryAsync(cancellationToken);

        await using var deleteMetrics = new NpgsqlCommand("""
            delete from processing_worker_endpoint_metrics
            where worker_id = @worker_id;
            """, connection, transaction);
        deleteMetrics.Parameters.AddWithValue("worker_id", command.WorkerId);
        await deleteMetrics.ExecuteNonQueryAsync(cancellationToken);

        foreach (var metric in command.EndpointMetrics ?? Array.Empty<WorkerEndpointMetric>())
        {
            await using var insertMetric = new NpgsqlCommand("""
                insert into processing_worker_endpoint_metrics (
                    worker_id,
                    processor_key,
                    capability,
                    endpoint,
                    enabled,
                    health,
                    in_flight,
                    concurrency_limit,
                    heartbeat_at,
                    updated_at)
                values (
                    @worker_id,
                    @processor_key,
                    @capability,
                    @endpoint,
                    @enabled,
                    @health,
                    @in_flight,
                    @concurrency_limit,
                    @heartbeat_at,
                    @heartbeat_at);
                """, connection, transaction);
            insertMetric.Parameters.AddWithValue("worker_id", command.WorkerId);
            insertMetric.Parameters.AddWithValue("processor_key", command.ProcessorKey);
            insertMetric.Parameters.AddWithValue("capability", command.Capability);
            insertMetric.Parameters.AddWithValue("endpoint", metric.Endpoint);
            insertMetric.Parameters.AddWithValue("enabled", metric.Enabled);
            insertMetric.Parameters.AddWithValue("health", metric.Health);
            insertMetric.Parameters.AddWithValue("in_flight", metric.InFlight);
            insertMetric.Parameters.AddWithValue("concurrency_limit", metric.ConcurrencyLimit);
            insertMetric.Parameters.AddWithValue("heartbeat_at", command.HeartbeatAt);
            await insertMetric.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
