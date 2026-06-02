using System.Text.Json;
using CenteralES.AccessControl;
using CenteralES.Admin;
using Npgsql;
using NpgsqlTypes;

namespace CenteralES.Infrastructure.AccessControl;

public sealed class PostgresAdminUserStore : IAdminUserStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminUserStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<AdminUserListItem>> ListAsync(
        AdminUserListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 200);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                id,
                login,
                role,
                is_active,
                created_at,
                updated_at,
                last_login_at,
                disabled_at
            from admin_users
            where (@login::text is null or lower(login) = lower(@login))
              and (@is_active::boolean is null or is_active = @is_active)
            order by created_at desc
            limit @limit;
            """, connection);
        command.Parameters.AddWithValue("login", (object?)query.Login ?? DBNull.Value);
        command.Parameters.AddWithValue("is_active", (object?)query.IsActive ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);

        var users = new List<AdminUserListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    public async Task<AdminCreateUserResult> CreateAsync(
        AdminCreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var passwordHash = AdminPasswordHasher.HashPassword(command.Password);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await LoginExistsAsync(connection, transaction, command.Login, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminCreateUserConflict(command.Login);
        }

        await using var insert = new NpgsqlCommand("""
            insert into admin_users (
                id,
                login,
                password_hash,
                is_active,
                role,
                created_at,
                updated_at)
            values (
                @id,
                @login,
                @password_hash,
                true,
                'admin',
                @created_at,
                @created_at)
            returning
                id,
                login,
                role,
                is_active,
                created_at,
                updated_at,
                last_login_at,
                disabled_at;
            """, connection, transaction);
        insert.Parameters.AddWithValue("id", userId);
        insert.Parameters.AddWithValue("login", command.Login);
        insert.Parameters.AddWithValue("password_hash", passwordHash);
        insert.Parameters.AddWithValue("created_at", command.RequestedAt);

        await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Admin user insert did not return a row.");
        }

        var user = ReadUser(reader);
        await reader.DisposeAsync();

        await InsertAuditAsync(
            connection,
            transaction,
            auditId,
            command.RequestedAt,
            command.ActorAdminId,
            command.ActorLogin,
            AdminAuditActions.CreateAdminUser,
            user.UserId,
            oldValue: null,
            newValue: new
            {
                userId = user.UserId.ToString("N"),
                login = user.Login,
                role = user.Role,
                isActive = user.IsActive
            },
            command.Comment,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new AdminCreateUserSuccess(user, auditId);
    }

    public async Task<AdminDisableUserResult> DisableAsync(
        AdminDisableUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var auditId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await ReadUserForUpdateAsync(connection, transaction, command.UserId, cancellationToken);
        if (current is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminDisableUserNotFound(command.UserId);
        }

        if (!current.IsActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminDisableUserConflict(command.UserId);
        }

        await using var update = new NpgsqlCommand("""
            update admin_users
            set is_active = false,
                disabled_at = @disabled_at,
                updated_at = @disabled_at
            where id = @id
            returning
                id,
                login,
                role,
                is_active,
                created_at,
                updated_at,
                last_login_at,
                disabled_at;
            """, connection, transaction);
        update.Parameters.AddWithValue("id", command.UserId);
        update.Parameters.AddWithValue("disabled_at", command.RequestedAt);

        await using var reader = await update.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Admin user disable did not return a row.");
        }

        var disabled = ReadUser(reader);
        await reader.DisposeAsync();

        await RevokeSessionsAsync(connection, transaction, command.UserId, command.RequestedAt, cancellationToken);
        await InsertAuditAsync(
            connection,
            transaction,
            auditId,
            command.RequestedAt,
            command.ActorAdminId,
            command.ActorLogin,
            AdminAuditActions.DisableAdminUser,
            disabled.UserId,
            oldValue: new
            {
                userId = current.UserId.ToString("N"),
                login = current.Login,
                isActive = true
            },
            newValue: new
            {
                userId = disabled.UserId.ToString("N"),
                login = disabled.Login,
                isActive = false,
                disabledAt = disabled.DisabledAt
            },
            command.Comment,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new AdminDisableUserSuccess(disabled, auditId);
    }

    public async Task<AdminChangeUserPasswordResult> ChangePasswordAsync(
        AdminChangeUserPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var auditId = Guid.NewGuid();
        var passwordHash = AdminPasswordHasher.HashPassword(command.Password);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await ReadUserForUpdateAsync(connection, transaction, command.UserId, cancellationToken);
        if (current is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminChangeUserPasswordNotFound(command.UserId);
        }

        if (!current.IsActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminChangeUserPasswordConflict(command.UserId);
        }

        await using var update = new NpgsqlCommand("""
            update admin_users
            set password_hash = @password_hash,
                updated_at = @updated_at
            where id = @id
            returning
                id,
                login,
                role,
                is_active,
                created_at,
                updated_at,
                last_login_at,
                disabled_at;
            """, connection, transaction);
        update.Parameters.AddWithValue("id", command.UserId);
        update.Parameters.AddWithValue("password_hash", passwordHash);
        update.Parameters.AddWithValue("updated_at", command.RequestedAt);

        await using var reader = await update.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Admin password change did not return a row.");
        }

        var user = ReadUser(reader);
        await reader.DisposeAsync();

        await RevokeSessionsExceptActorAsync(
            connection,
            transaction,
            command.UserId,
            command.ActorAdminId,
            command.RequestedAt,
            cancellationToken);
        await InsertAuditAsync(
            connection,
            transaction,
            auditId,
            command.RequestedAt,
            command.ActorAdminId,
            command.ActorLogin,
            AdminAuditActions.ChangeAdminPassword,
            user.UserId,
            oldValue: new
            {
                userId = user.UserId.ToString("N"),
                login = user.Login
            },
            newValue: new
            {
                userId = user.UserId.ToString("N"),
                login = user.Login,
                passwordChanged = true
            },
            command.Comment,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new AdminChangeUserPasswordSuccess(user, auditId);
    }

    private static async Task<bool> LoginExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string login,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1
                from admin_users
                where lower(login) = lower(@login)
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("login", login);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Admin user existence query returned no value."));
    }

    private static async Task<AdminUserListItem?> ReadUserForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select
                id,
                login,
                role,
                is_active,
                created_at,
                updated_at,
                last_login_at,
                disabled_at
            from admin_users
            where id = @id
            for update;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    private static async Task RevokeSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            update admin_sessions
            set revoked_at = @revoked_at
            where admin_user_id = @admin_user_id
              and revoked_at is null;
            """, connection, transaction);
        command.Parameters.AddWithValue("admin_user_id", userId);
        command.Parameters.AddWithValue("revoked_at", revokedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RevokeSessionsExceptActorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid actorAdminId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        if (userId == actorAdminId)
        {
            return;
        }

        await RevokeSessionsAsync(connection, transaction, userId, revokedAt, cancellationToken);
    }

    private static AdminUserListItem ReadUser(NpgsqlDataReader reader)
    {
        return new AdminUserListItem(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid auditId,
        DateTimeOffset occurredAt,
        Guid actorAdminId,
        string actorLogin,
        string action,
        Guid targetUserId,
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
        audit.Parameters.AddWithValue("target_type", AdminAuditTargetTypes.AdminUser);
        audit.Parameters.AddWithValue("target_id", targetUserId.ToString("N"));
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
