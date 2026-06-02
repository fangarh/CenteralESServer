namespace CenteralES.Admin;

public sealed record AdminUserListQuery(
    string? Login,
    bool? IsActive,
    int Limit);

public sealed record AdminUserListItem(
    Guid UserId,
    string Login,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DisabledAt);

public sealed record AdminCreateUserCommand(
    string Login,
    string Password,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminCreateUserSuccess(AdminUserListItem User, Guid AuditId) : AdminCreateUserResult;

public sealed record AdminCreateUserConflict(string Login) : AdminCreateUserResult;

public abstract record AdminCreateUserResult;

public sealed record AdminDisableUserCommand(
    Guid UserId,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminDisableUserSuccess(AdminUserListItem User, Guid AuditId) : AdminDisableUserResult;

public sealed record AdminDisableUserNotFound(Guid UserId) : AdminDisableUserResult;

public sealed record AdminDisableUserConflict(Guid UserId) : AdminDisableUserResult;

public abstract record AdminDisableUserResult;

public sealed record AdminChangeUserPasswordCommand(
    Guid UserId,
    string Password,
    Guid ActorAdminId,
    string ActorLogin,
    DateTimeOffset RequestedAt,
    string? Comment,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminChangeUserPasswordSuccess(AdminUserListItem User, Guid AuditId) : AdminChangeUserPasswordResult;

public sealed record AdminChangeUserPasswordNotFound(Guid UserId) : AdminChangeUserPasswordResult;

public sealed record AdminChangeUserPasswordConflict(Guid UserId) : AdminChangeUserPasswordResult;

public abstract record AdminChangeUserPasswordResult;

public interface IAdminUserStore
{
    Task<IReadOnlyList<AdminUserListItem>> ListAsync(
        AdminUserListQuery query,
        CancellationToken cancellationToken);

    Task<AdminCreateUserResult> CreateAsync(
        AdminCreateUserCommand command,
        CancellationToken cancellationToken);

    Task<AdminDisableUserResult> DisableAsync(
        AdminDisableUserCommand command,
        CancellationToken cancellationToken);

    Task<AdminChangeUserPasswordResult> ChangePasswordAsync(
        AdminChangeUserPasswordCommand command,
        CancellationToken cancellationToken);
}
