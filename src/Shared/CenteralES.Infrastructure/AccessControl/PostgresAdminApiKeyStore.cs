using System.Text.Json;
using CenteralES.AccessControl;
using CenteralES.Admin;
using Npgsql;
using NpgsqlTypes;

namespace CenteralES.Infrastructure.AccessControl;

public sealed class PostgresAdminApiKeyStore : IAdminApiKeyStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminApiKeyStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<AdminApiKeyListItem>> ListAsync(
        AdminApiKeyListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 200);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                key_id,
                name,
                is_active,
                allowed_capabilities,
                created_at,
                updated_at,
                expires_at,
                last_used_at,
                disabled_at
            from client_applications
            where (@key_id::text is null or key_id = @key_id)
              and (@is_active::boolean is null or is_active = @is_active)
            order by created_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("key_id", (object?)query.KeyId ?? DBNull.Value);
        command.Parameters.AddWithValue("is_active", (object?)query.IsActive ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);

        var keys = new List<AdminApiKeyListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(ReadKey(reader));
        }

        return keys;
    }

    public async Task<AdminCreateApiKeyResult> CreateAsync(
        AdminCreateApiKeyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var secret = SecureToken.Generate();
        var secretHash = ApiKeySecretHasher.HashSecret(secret);
        var auditId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var exists = await ApiKeyExistsAsync(connection, transaction, command.KeyId, cancellationToken);
        if (exists)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminCreateApiKeyConflict(command.KeyId);
        }

        await using var insert = new NpgsqlCommand("""
            insert into client_applications (
                key_id,
                name,
                secret_hash,
                is_active,
                allowed_capabilities,
                created_at,
                updated_at,
                expires_at)
            values (
                @key_id,
                @name,
                @secret_hash,
                true,
                @allowed_capabilities,
                @created_at,
                @created_at,
                @expires_at)
            returning
                key_id,
                name,
                is_active,
                allowed_capabilities,
                created_at,
                updated_at,
                expires_at,
                last_used_at,
                disabled_at;
            """, connection, transaction);
        insert.Parameters.AddWithValue("key_id", command.KeyId);
        insert.Parameters.AddWithValue("name", command.Name);
        insert.Parameters.AddWithValue("secret_hash", secretHash);
        insert.Parameters.AddWithValue("allowed_capabilities", command.AllowedCapabilities.ToArray());
        insert.Parameters.AddWithValue("created_at", command.RequestedAt);
        insert.Parameters.AddWithValue("expires_at", (object?)command.ExpiresAt ?? DBNull.Value);

        await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("API key insert did not return a row.");
        }

        var key = ReadKey(reader);
        await reader.DisposeAsync();

        await InsertAuditAsync(
            connection,
            transaction,
            auditId,
            command.RequestedAt,
            command.ActorAdminId,
            command.ActorLogin,
            AdminAuditActions.CreateApiKey,
            command.KeyId,
            oldValue: null,
            newValue: new
            {
                keyId = command.KeyId,
                name = command.Name,
                isActive = true,
                allowedCapabilities = command.AllowedCapabilities,
                expiresAt = command.ExpiresAt
            },
            command.Comment,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new AdminCreateApiKeySuccess(key, secret, auditId);
    }

    public async Task<AdminDisableApiKeyResult> DisableAsync(
        AdminDisableApiKeyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var auditId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await ReadKeyForUpdateAsync(connection, transaction, command.KeyId, cancellationToken);
        if (current is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminDisableApiKeyNotFound(command.KeyId);
        }

        if (!current.IsActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminDisableApiKeyConflict(command.KeyId);
        }

        await using var update = new NpgsqlCommand("""
            update client_applications
            set is_active = false,
                disabled_at = @disabled_at,
                updated_at = @disabled_at
            where key_id = @key_id
            returning
                key_id,
                name,
                is_active,
                allowed_capabilities,
                created_at,
                updated_at,
                expires_at,
                last_used_at,
                disabled_at;
            """, connection, transaction);
        update.Parameters.AddWithValue("key_id", command.KeyId);
        update.Parameters.AddWithValue("disabled_at", command.RequestedAt);

        await using var reader = await update.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("API key disable did not return a row.");
        }

        var disabled = ReadKey(reader);
        await reader.DisposeAsync();

        await InsertAuditAsync(
            connection,
            transaction,
            auditId,
            command.RequestedAt,
            command.ActorAdminId,
            command.ActorLogin,
            AdminAuditActions.DisableApiKey,
            command.KeyId,
            oldValue: new
            {
                keyId = current.KeyId,
                isActive = true
            },
            newValue: new
            {
                keyId = disabled.KeyId,
                isActive = false,
                disabledAt = disabled.DisabledAt
            },
            command.Comment,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new AdminDisableApiKeySuccess(disabled, auditId);
    }

    private static async Task<bool> ApiKeyExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string keyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1
                from client_applications
                where key_id = @key_id
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("key_id", keyId);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("API key existence query returned no value."));
    }

    private static async Task<AdminApiKeyListItem?> ReadKeyForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string keyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                key_id,
                name,
                is_active,
                allowed_capabilities,
                created_at,
                updated_at,
                expires_at,
                last_used_at,
                disabled_at
            from client_applications
            where key_id = @key_id
            for update;
            """, connection, transaction);
        command.Parameters.AddWithValue("key_id", keyId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadKey(reader) : null;
    }

    private static AdminApiKeyListItem ReadKey(NpgsqlDataReader reader)
    {
        return new AdminApiKeyListItem(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetFieldValue<string[]>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8));
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid auditId,
        DateTimeOffset occurredAt,
        Guid actorAdminId,
        string actorLogin,
        string action,
        string keyId,
        object? oldValue,
        object? newValue,
        string? comment,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await using var audit = new NpgsqlCommand("""
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
        audit.Parameters.AddWithValue("id", auditId);
        audit.Parameters.AddWithValue("occurred_at", occurredAt);
        audit.Parameters.AddWithValue("actor_admin_id", actorAdminId);
        audit.Parameters.AddWithValue("actor_login", actorLogin);
        audit.Parameters.AddWithValue("action", action);
        audit.Parameters.AddWithValue("target_type", AdminAuditTargetTypes.ApiKey);
        audit.Parameters.AddWithValue("target_id", keyId);
        AddNullableJsonParameter(audit, "old_value_json", oldValue);
        AddNullableJsonParameter(audit, "new_value_json", newValue);
        audit.Parameters.AddWithValue("comment", (object?)NormalizeComment(comment) ?? DBNull.Value);
        audit.Parameters.AddWithValue("correlation_id", Guid.NewGuid().ToString("N"));
        audit.Parameters.AddWithValue("ip", (object?)ipAddress ?? DBNull.Value);
        audit.Parameters.AddWithValue("user_agent", (object?)userAgent ?? DBNull.Value);
        AddNullableJsonParameter(audit, "technical_metadata_json", new
        {
            source = "admin_api"
        });

        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NormalizeComment(string? comment)
    {
        return string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    private static void AddNullableJsonParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value)
        });
    }
}
