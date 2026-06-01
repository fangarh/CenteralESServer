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
            """, connection);
        upsert.Parameters.AddWithValue("worker_id", command.WorkerId);
        upsert.Parameters.AddWithValue("processor_key", command.ProcessorKey);
        upsert.Parameters.AddWithValue("capability", command.Capability);
        upsert.Parameters.AddWithValue("started_at", command.StartedAt);
        upsert.Parameters.AddWithValue("heartbeat_at", command.HeartbeatAt);

        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }
}
