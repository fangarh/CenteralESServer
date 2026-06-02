using System.Data;
using System.Text.Json;
using CenteralES.AccessControl;
using CenteralES.Admin;
using Npgsql;
using NpgsqlTypes;

namespace CenteralES.Infrastructure.AccessControl;

public sealed class PostgresAdminBootstrapper : IAdminBootstrapper
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminBootstrapper(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select count(*)
            from admin_users
            where is_active = true;
            """, connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<AdminBootstrapUserResult> BootstrapFirstAdminAsync(
        AdminBootstrapUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationError = AdminBootstrapValidator.Validate(command, AdminPasswordHasher.MinimumPasswordLength);
        if (validationError is not null)
        {
            return new AdminBootstrapInvalidInput(validationError);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        await AcquireBootstrapLockAsync(connection, transaction, cancellationToken);

        var activeAdminCount = await CountActiveAdminsAsync(connection, transaction, cancellationToken);
        if (activeAdminCount > 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminBootstrapAlreadyInitialized(activeAdminCount);
        }

        if (await LoginExistsAsync(connection, transaction, command.Login.Trim(), cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminBootstrapLoginConflict(command.Login.Trim());
        }

        var userId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var login = command.Login.Trim();
        var passwordHash = AdminPasswordHasher.HashPassword(command.Password);

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
        insert.Parameters.AddWithValue("login", login);
        insert.Parameters.AddWithValue("password_hash", passwordHash);
        insert.Parameters.AddWithValue("created_at", command.RequestedAt);

        await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Admin bootstrap insert did not return a row.");
        }

        var user = ReadUser(reader);
        await reader.DisposeAsync();

        await InsertAuditAsync(connection, transaction, auditId, command, user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminBootstrapUserSuccess(user, auditId);
    }

    private static async Task AcquireBootstrapLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select pg_advisory_xact_lock(hashtext('centerales_admin_bootstrap'));
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountActiveAdminsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select count(*)
            from admin_users
            where is_active = true;
            """, connection, transaction);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
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
            ?? throw new InvalidOperationException("Admin bootstrap login existence query returned no value."));
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
        AdminBootstrapUserCommand command,
        AdminUserListItem user,
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
                null,
                @actor_login,
                @action,
                @target_type,
                @target_id,
                null,
                @new_value_json,
                @comment,
                @correlation_id,
                null,
                null,
                @technical_metadata_json);
            """, connection, transaction);
        audit.Parameters.AddWithValue("id", auditId);
        audit.Parameters.AddWithValue("occurred_at", command.RequestedAt);
        audit.Parameters.AddWithValue("actor_login", "bootstrap");
        audit.Parameters.AddWithValue("action", AdminAuditActions.BootstrapAdminUser);
        audit.Parameters.AddWithValue("target_type", AdminAuditTargetTypes.AdminUser);
        audit.Parameters.AddWithValue("target_id", user.UserId.ToString("N"));
        audit.Parameters.Add(new NpgsqlParameter("new_value_json", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(new
            {
                userId = user.UserId.ToString("N"),
                login = user.Login,
                role = user.Role,
                isActive = user.IsActive
            })
        });
        audit.Parameters.AddWithValue("comment", (object?)NormalizeComment(command.Comment) ?? DBNull.Value);
        audit.Parameters.AddWithValue("correlation_id", Guid.NewGuid().ToString("N"));
        audit.Parameters.Add(new NpgsqlParameter("technical_metadata_json", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(new
            {
                source = command.Source
            })
        });

        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NormalizeComment(string? comment)
    {
        return string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }
}
