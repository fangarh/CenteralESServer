namespace CenteralES.AccessControl;

public sealed record AdminLoginRequest(
    string Login,
    string Password,
    DateTimeOffset RequestedAt,
    string? IpAddress,
    string? UserAgent);

public sealed record AdminLoginOutcome(
    AdminLoginStatus Status,
    AdminPrincipal? Principal = null,
    AdminSessionCredential? Credential = null)
{
    public static AdminLoginOutcome Success(AdminPrincipal principal, AdminSessionCredential credential)
    {
        return new AdminLoginOutcome(AdminLoginStatus.Success, principal, credential);
    }

    public static AdminLoginOutcome Unauthorized()
    {
        return new AdminLoginOutcome(AdminLoginStatus.Unauthorized);
    }
}

public sealed record AdminSessionValidationRequest(
    string? SessionToken,
    string? CsrfToken,
    bool RequireCsrf,
    DateTimeOffset RequestedAt);

public sealed record AdminSessionValidationOutcome(
    AdminSessionValidationStatus Status,
    AdminPrincipal? Principal = null)
{
    public static AdminSessionValidationOutcome Success(AdminPrincipal principal)
    {
        return new AdminSessionValidationOutcome(AdminSessionValidationStatus.Success, principal);
    }

    public static AdminSessionValidationOutcome Unauthorized()
    {
        return new AdminSessionValidationOutcome(AdminSessionValidationStatus.Unauthorized);
    }

    public static AdminSessionValidationOutcome Forbidden()
    {
        return new AdminSessionValidationOutcome(AdminSessionValidationStatus.Forbidden);
    }
}

public sealed record AdminPrincipal(
    Guid UserId,
    string Login,
    string Role);

public sealed record AdminSessionCredential(
    string SessionToken,
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt);

public enum AdminLoginStatus
{
    Success,
    Unauthorized
}

public enum AdminSessionValidationStatus
{
    Success,
    Unauthorized,
    Forbidden
}

public interface IAdminAuthenticator
{
    Task<AdminLoginOutcome> LoginAsync(
        AdminLoginRequest request,
        CancellationToken cancellationToken);

    Task<AdminSessionValidationOutcome> ValidateSessionAsync(
        AdminSessionValidationRequest request,
        CancellationToken cancellationToken);

    Task LogoutAsync(
        string? sessionToken,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken);
}
