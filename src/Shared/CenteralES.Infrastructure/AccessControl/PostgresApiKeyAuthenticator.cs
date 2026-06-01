using CenteralES.AccessControl;
using Npgsql;

namespace CenteralES.Infrastructure.AccessControl;

public sealed class PostgresApiKeyAuthenticator : IApiKeyAuthenticator
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresApiKeyAuthenticator(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<ApiKeyAuthenticationOutcome> AuthenticateAsync(
        ApiKeyAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                key_id,
                secret_hash,
                is_active,
                allowed_capabilities,
                expires_at
            from client_applications
            where key_id = @key_id;
            """, connection);
        command.Parameters.AddWithValue("key_id", request.KeyId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return ApiKeyAuthenticationOutcome.Unauthorized();
        }

        var keyId = reader.GetString(0);
        var secretHash = reader.GetString(1);
        var isActive = reader.GetBoolean(2);
        var allowedCapabilities = reader.GetFieldValue<string[]>(3);
        DateTimeOffset? expiresAt = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4);
        await reader.DisposeAsync();

        if (!isActive || expiresAt <= request.UsedAt)
        {
            return ApiKeyAuthenticationOutcome.Unauthorized();
        }

        if (!ApiKeySecretHasher.VerifySecret(request.Secret, secretHash))
        {
            return ApiKeyAuthenticationOutcome.Unauthorized();
        }

        if (!allowedCapabilities.Contains(request.RequiredCapability, StringComparer.Ordinal))
        {
            return ApiKeyAuthenticationOutcome.Forbidden(keyId);
        }

        await UpdateLastUsedAsync(connection, request, cancellationToken);
        return ApiKeyAuthenticationOutcome.Success(keyId);
    }

    private static async Task UpdateLastUsedAsync(
        NpgsqlConnection connection,
        ApiKeyAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand("""
            update client_applications
            set last_used_at = @last_used_at,
                last_used_ip = @last_used_ip,
                last_used_user_agent = @last_used_user_agent,
                updated_at = @last_used_at
            where key_id = @key_id;
            """, connection);
        update.Parameters.AddWithValue("last_used_at", request.UsedAt);
        update.Parameters.AddWithValue("last_used_ip", (object?)request.IpAddress ?? DBNull.Value);
        update.Parameters.AddWithValue("last_used_user_agent", (object?)request.UserAgent ?? DBNull.Value);
        update.Parameters.AddWithValue("key_id", request.KeyId);

        await update.ExecuteNonQueryAsync(cancellationToken);
    }
}
