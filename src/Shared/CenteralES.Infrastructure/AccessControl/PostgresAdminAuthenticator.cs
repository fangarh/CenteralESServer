using CenteralES.AccessControl;
using Npgsql;

namespace CenteralES.Infrastructure.AccessControl;

public sealed class PostgresAdminAuthenticator : IAdminAuthenticator
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(8);
    private static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(24);

    private readonly NpgsqlDataSource _dataSource;

    public PostgresAdminAuthenticator(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<AdminLoginOutcome> LoginAsync(
        AdminLoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var login = request.Login.Trim();
        if (string.IsNullOrWhiteSpace(login)
            || string.IsNullOrWhiteSpace(request.Password)
            || request.Password.Length < AdminPasswordHasher.MinimumPasswordLength)
        {
            return AdminLoginOutcome.Unauthorized();
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, login, password_hash, is_active, role
            from admin_users
            where lower(login) = lower(@login);
            """, connection);
        command.Parameters.AddWithValue("login", login);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return AdminLoginOutcome.Unauthorized();
        }

        var userId = reader.GetGuid(0);
        var storedLogin = reader.GetString(1);
        var passwordHash = reader.GetString(2);
        var isActive = reader.GetBoolean(3);
        var role = reader.GetString(4);
        await reader.DisposeAsync();

        if (!isActive || !AdminPasswordHasher.VerifyPassword(request.Password, passwordHash))
        {
            return AdminLoginOutcome.Unauthorized();
        }

        var sessionToken = SecureToken.Generate();
        var csrfToken = SecureToken.Generate();
        var expiresAt = request.RequestedAt.Add(AbsoluteTimeout);
        var idleExpiresAt = request.RequestedAt.Add(IdleTimeout);
        var sessionId = Guid.NewGuid();

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand("""
            insert into admin_sessions (
                id,
                admin_user_id,
                session_token_hash,
                csrf_token_hash,
                created_at,
                last_seen_at,
                expires_at,
                idle_expires_at,
                created_ip,
                created_user_agent)
            values (
                @id,
                @admin_user_id,
                @session_token_hash,
                @csrf_token_hash,
                @created_at,
                @last_seen_at,
                @expires_at,
                @idle_expires_at,
                @created_ip,
                @created_user_agent);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", sessionId);
            insert.Parameters.AddWithValue("admin_user_id", userId);
            insert.Parameters.AddWithValue("session_token_hash", SecureToken.Hash(sessionToken));
            insert.Parameters.AddWithValue("csrf_token_hash", SecureToken.Hash(csrfToken));
            insert.Parameters.AddWithValue("created_at", request.RequestedAt);
            insert.Parameters.AddWithValue("last_seen_at", request.RequestedAt);
            insert.Parameters.AddWithValue("expires_at", expiresAt);
            insert.Parameters.AddWithValue("idle_expires_at", idleExpiresAt);
            insert.Parameters.AddWithValue("created_ip", (object?)request.IpAddress ?? DBNull.Value);
            insert.Parameters.AddWithValue("created_user_agent", (object?)request.UserAgent ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var update = new NpgsqlCommand("""
            update admin_users
            set last_login_at = @last_login_at,
                updated_at = @last_login_at
            where id = @id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("last_login_at", request.RequestedAt);
            update.Parameters.AddWithValue("id", userId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return AdminLoginOutcome.Success(
            new AdminPrincipal(userId, storedLogin, role),
            new AdminSessionCredential(sessionToken, csrfToken, expiresAt, idleExpiresAt));
    }

    public async Task<AdminSessionValidationOutcome> ValidateSessionAsync(
        AdminSessionValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SessionToken))
        {
            return AdminSessionValidationOutcome.Unauthorized();
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                session.id,
                session.csrf_token_hash,
                session.expires_at,
                session.idle_expires_at,
                user_account.id,
                user_account.login,
                user_account.role,
                user_account.is_active
            from admin_sessions session
            inner join admin_users user_account on user_account.id = session.admin_user_id
            where session.session_token_hash = @session_token_hash
              and session.revoked_at is null;
            """, connection);
        command.Parameters.AddWithValue("session_token_hash", SecureToken.Hash(request.SessionToken));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return AdminSessionValidationOutcome.Unauthorized();
        }

        var sessionId = reader.GetGuid(0);
        var csrfTokenHash = reader.GetString(1);
        var expiresAt = reader.GetFieldValue<DateTimeOffset>(2);
        var idleExpiresAt = reader.GetFieldValue<DateTimeOffset>(3);
        var userId = reader.GetGuid(4);
        var login = reader.GetString(5);
        var role = reader.GetString(6);
        var isActive = reader.GetBoolean(7);
        await reader.DisposeAsync();

        if (!isActive || expiresAt <= request.RequestedAt || idleExpiresAt <= request.RequestedAt)
        {
            return AdminSessionValidationOutcome.Unauthorized();
        }

        if (request.RequireCsrf && !SecureToken.Verify(request.CsrfToken ?? string.Empty, csrfTokenHash))
        {
            return AdminSessionValidationOutcome.Forbidden();
        }

        await using var update = new NpgsqlCommand("""
            update admin_sessions
            set last_seen_at = @last_seen_at,
                idle_expires_at = @idle_expires_at
            where id = @id;
            """, connection);
        update.Parameters.AddWithValue("last_seen_at", request.RequestedAt);
        update.Parameters.AddWithValue("idle_expires_at", Min(request.RequestedAt.Add(IdleTimeout), expiresAt));
        update.Parameters.AddWithValue("id", sessionId);
        await update.ExecuteNonQueryAsync(cancellationToken);

        return AdminSessionValidationOutcome.Success(new AdminPrincipal(userId, login, role));
    }

    public async Task LogoutAsync(
        string? sessionToken,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update admin_sessions
            set revoked_at = @revoked_at
            where session_token_hash = @session_token_hash
              and revoked_at is null;
            """, connection);
        command.Parameters.AddWithValue("revoked_at", requestedAt);
        command.Parameters.AddWithValue("session_token_hash", SecureToken.Hash(sessionToken));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left <= right ? left : right;
    }
}
