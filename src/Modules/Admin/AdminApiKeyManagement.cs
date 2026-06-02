namespace CenteralES.Admin;

public sealed record AdminApiKeyListQuery(
    string? KeyId,
    bool? IsActive,
    int Limit);

public sealed record AdminApiKeyListItem(
    string KeyId,
    string Name,
    bool IsActive,
    IReadOnlyList<string> AllowedCapabilities,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? DisabledAt);

public sealed record AdminCreateApiKeyCommand(
    string KeyId,
    string Name,
    IReadOnlyList<string> AllowedCapabilities,
    DateTimeOffset? ExpiresAt,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminCreateApiKeySuccess(
    AdminApiKeyListItem Key,
    string Secret,
    Guid AuditId) : AdminCreateApiKeyResult;

public sealed record AdminCreateApiKeyConflict(string KeyId) : AdminCreateApiKeyResult;

public abstract record AdminCreateApiKeyResult;

public sealed record AdminDisableApiKeyCommand(
    string KeyId,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminDisableApiKeySuccess(
    AdminApiKeyListItem Key,
    Guid AuditId) : AdminDisableApiKeyResult;

public sealed record AdminDisableApiKeyNotFound(string KeyId) : AdminDisableApiKeyResult;

public sealed record AdminDisableApiKeyConflict(string KeyId) : AdminDisableApiKeyResult;

public abstract record AdminDisableApiKeyResult;

public interface IAdminApiKeyStore
{
    Task<IReadOnlyList<AdminApiKeyListItem>> ListAsync(
        AdminApiKeyListQuery query,
        CancellationToken cancellationToken);

    Task<AdminCreateApiKeyResult> CreateAsync(
        AdminCreateApiKeyCommand command,
        CancellationToken cancellationToken);

    Task<AdminDisableApiKeyResult> DisableAsync(
        AdminDisableApiKeyCommand command,
        CancellationToken cancellationToken);
}
