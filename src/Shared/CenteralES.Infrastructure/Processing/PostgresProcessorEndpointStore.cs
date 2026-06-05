using System.Text.Json;
using CenteralES.Admin;
using CenteralES.Processing;
using Npgsql;
using NpgsqlTypes;

namespace CenteralES.Infrastructure.Processing;

public sealed class PostgresProcessorEndpointStore : IAdminProcessorEndpointStore, IProcessorEndpointConfigurationStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProcessorEndpointStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<ProcessorEndpointConfiguration>> ListProcessorEndpointsAsync(
        string processorKey,
        string capability,
        CancellationToken cancellationToken)
    {
        var endpoints = await ListDbEndpointsAsync(processorKey, capability, cancellationToken);

        return endpoints
            .Select(endpoint => new ProcessorEndpointConfiguration(
                endpoint.Endpoint,
                endpoint.Enabled,
                endpoint.ConcurrencyLimit,
                endpoint.Source))
            .ToArray();
    }

    public async Task<IReadOnlyList<AdminProcessorEndpointListItem>> ListDbEndpointsAsync(
        string processorKey,
        string capability,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                id,
                processor_key,
                capability,
                endpoint,
                enabled,
                concurrency_limit,
                priority,
                created_at,
                updated_at,
                disabled_at
            from processor_endpoints
            where processor_key = @processor_key
              and capability = @capability
            order by enabled desc, priority asc, endpoint_normalized asc;
            """, connection);
        command.Parameters.AddWithValue("processor_key", processorKey);
        command.Parameters.AddWithValue("capability", capability);

        var endpoints = new List<AdminProcessorEndpointListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            endpoints.Add(ReadEndpoint(reader));
        }

        return endpoints;
    }

    public async Task<AdminCreateProcessorEndpointResult> CreateAsync(
        AdminCreateProcessorEndpointCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedEndpoint = ProcessorEndpointNormalizer.Normalize(command.Endpoint);
        var endpointId = Guid.NewGuid();
        var auditId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var insert = new NpgsqlCommand("""
                insert into processor_endpoints (
                    id,
                    processor_key,
                    capability,
                    endpoint,
                    endpoint_normalized,
                    enabled,
                    concurrency_limit,
                    priority,
                    created_at,
                    updated_at,
                    disabled_at)
                values (
                    @id,
                    @processor_key,
                    @capability,
                    @endpoint,
                    @endpoint_normalized,
                    true,
                    @concurrency_limit,
                    @priority,
                    @created_at,
                    @created_at,
                    null);
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", endpointId);
            insert.Parameters.AddWithValue("processor_key", command.ProcessorKey);
            insert.Parameters.AddWithValue("capability", command.Capability);
            insert.Parameters.AddWithValue("endpoint", command.Endpoint);
            insert.Parameters.AddWithValue("endpoint_normalized", normalizedEndpoint);
            insert.Parameters.AddWithValue("concurrency_limit", command.ConcurrencyLimit);
            insert.Parameters.AddWithValue("priority", command.Priority);
            insert.Parameters.AddWithValue("created_at", command.RequestedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);

            await InsertAuditEventAsync(
                connection,
                transaction,
                auditId,
                command.ActorAdminId,
                command.ActorLogin,
                command.RequestedAt,
                AdminAuditActions.CreateProcessorEndpoint,
                endpointId,
                oldValue: null,
                new
                {
                    processorKey = command.ProcessorKey,
                    capability = command.Capability,
                    endpoint = normalizedEndpoint,
                    enabled = true,
                    concurrencyLimit = command.ConcurrencyLimit,
                    priority = command.Priority
                },
                command.Comment,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AdminCreateProcessorEndpointConflict();
        }

        return new AdminCreateProcessorEndpointSuccess(
            new AdminProcessorEndpointListItem(
                endpointId,
                command.ProcessorKey,
                command.Capability,
                command.Endpoint,
                Enabled: true,
                command.ConcurrencyLimit,
                command.Priority,
                "db",
                command.RequestedAt,
                command.RequestedAt,
                DisabledAt: null),
            auditId);
    }

    public async Task<AdminUpdateProcessorEndpointResult> UpdateAsync(
        AdminUpdateProcessorEndpointCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await ReadEndpointForUpdateAsync(
            connection,
            transaction,
            command.EndpointId,
            command.ProcessorKey,
            cancellationToken);
        if (existing is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminUpdateProcessorEndpointNotFound();
        }

        var nextEnabled = command.Enabled ?? existing.Enabled;
        var nextConcurrencyLimit = command.ConcurrencyLimit ?? existing.ConcurrencyLimit;
        var nextPriority = command.Priority ?? existing.Priority;
        var disabledAt = nextEnabled ? (DateTimeOffset?)null : command.RequestedAt;

        await using var update = new NpgsqlCommand("""
            update processor_endpoints
            set enabled = @enabled,
                concurrency_limit = @concurrency_limit,
                priority = @priority,
                updated_at = @updated_at,
                disabled_at = @disabled_at
            where id = @id;
            """, connection, transaction);
        update.Parameters.AddWithValue("enabled", nextEnabled);
        update.Parameters.AddWithValue("concurrency_limit", nextConcurrencyLimit);
        update.Parameters.AddWithValue("priority", nextPriority);
        update.Parameters.AddWithValue("updated_at", command.RequestedAt);
        update.Parameters.AddWithValue("disabled_at", (object?)disabledAt ?? DBNull.Value);
        update.Parameters.AddWithValue("id", command.EndpointId);
        await update.ExecuteNonQueryAsync(cancellationToken);

        var normalizedEndpoint = ProcessorEndpointNormalizer.Normalize(existing.Endpoint);
        var auditId = Guid.NewGuid();
        await InsertAuditEventAsync(
            connection,
            transaction,
            auditId,
            command.ActorAdminId,
            command.ActorLogin,
            command.RequestedAt,
            AdminAuditActions.UpdateProcessorEndpoint,
            command.EndpointId,
            new
            {
                endpoint = normalizedEndpoint,
                enabled = existing.Enabled,
                concurrencyLimit = existing.ConcurrencyLimit,
                priority = existing.Priority
            },
            new
            {
                endpoint = normalizedEndpoint,
                enabled = nextEnabled,
                concurrencyLimit = nextConcurrencyLimit,
                priority = nextPriority
            },
            command.Comment,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new AdminUpdateProcessorEndpointSuccess(
            existing with
            {
                Enabled = nextEnabled,
                ConcurrencyLimit = nextConcurrencyLimit,
                Priority = nextPriority,
                UpdatedAt = command.RequestedAt,
                DisabledAt = disabledAt
            },
            auditId);
    }

    private static async Task<AdminProcessorEndpointListItem?> ReadEndpointForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid endpointId,
        string processorKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                id,
                processor_key,
                capability,
                endpoint,
                enabled,
                concurrency_limit,
                priority,
                created_at,
                updated_at,
                disabled_at
            from processor_endpoints
            where id = @id
              and processor_key = @processor_key
            for update;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", endpointId);
        command.Parameters.AddWithValue("processor_key", processorKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadEndpoint(reader)
            : null;
    }

    private static AdminProcessorEndpointListItem ReadEndpoint(NpgsqlDataReader reader)
    {
        return new AdminProcessorEndpointListItem(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            "db",
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
    }

    private static async Task InsertAuditEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid auditId,
        Guid actorAdminId,
        string actorLogin,
        DateTimeOffset occurredAt,
        string action,
        Guid endpointId,
        object? oldValue,
        object newValue,
        string? comment,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await using var insertAudit = new NpgsqlCommand("""
            insert into admin_audit_events (
                id,
                occurred_at,
                actor_admin_id,
                actor_login,
                action,
                target_type,
                target_id,
                old_value_json,
                new_value_json,
                comment,
                correlation_id,
                ip,
                user_agent,
                technical_metadata_json)
            values (
                @id,
                @occurred_at,
                @actor_admin_id,
                @actor_login,
                @action,
                @target_type,
                @target_id,
                @old_value_json,
                @new_value_json,
                @comment,
                @correlation_id,
                @ip,
                @user_agent,
                @technical_metadata_json);
            """, connection, transaction);

        insertAudit.Parameters.AddWithValue("id", auditId);
        insertAudit.Parameters.AddWithValue("occurred_at", occurredAt);
        insertAudit.Parameters.AddWithValue("actor_admin_id", actorAdminId);
        insertAudit.Parameters.AddWithValue("actor_login", actorLogin);
        insertAudit.Parameters.AddWithValue("action", action);
        insertAudit.Parameters.AddWithValue("target_type", AdminAuditTargetTypes.ProcessorEndpoint);
        insertAudit.Parameters.AddWithValue("target_id", endpointId.ToString("N"));
        AddNullableJsonParameter(insertAudit, "old_value_json", oldValue);
        AddJsonParameter(insertAudit, "new_value_json", newValue);
        insertAudit.Parameters.AddWithValue("comment", (object?)NormalizeComment(comment) ?? DBNull.Value);
        insertAudit.Parameters.AddWithValue("correlation_id", Guid.NewGuid().ToString("N"));
        insertAudit.Parameters.AddWithValue("ip", (object?)ip ?? DBNull.Value);
        insertAudit.Parameters.AddWithValue("user_agent", (object?)userAgent ?? DBNull.Value);
        AddJsonParameter(insertAudit, "technical_metadata_json", new { source = "admin_api" });

        await insertAudit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NormalizeComment(string? comment)
    {
        return string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
    }

    private static void AddNullableJsonParameter(NpgsqlCommand command, string name, object? value)
    {
        if (value is null)
        {
            command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
            {
                Value = DBNull.Value
            });
            return;
        }

        AddJsonParameter(command, name, value);
    }

    private static void AddJsonParameter(NpgsqlCommand command, string name, object value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value)
        });
    }
}
