namespace CenteralES.AccessControl;

public sealed record ApiKeyAuthenticationRequest(
    string KeyId,
    string Secret,
    string RequiredCapability,
    DateTimeOffset UsedAt,
    string? IpAddress,
    string? UserAgent);

public sealed record ApiKeyAuthenticationOutcome(
    ApiKeyAuthenticationStatus Status,
    string? KeyId = null)
{
    public static ApiKeyAuthenticationOutcome Success(string keyId)
    {
        return new ApiKeyAuthenticationOutcome(ApiKeyAuthenticationStatus.Success, keyId);
    }

    public static ApiKeyAuthenticationOutcome Unauthorized()
    {
        return new ApiKeyAuthenticationOutcome(ApiKeyAuthenticationStatus.Unauthorized);
    }

    public static ApiKeyAuthenticationOutcome Forbidden(string keyId)
    {
        return new ApiKeyAuthenticationOutcome(ApiKeyAuthenticationStatus.Forbidden, keyId);
    }
}

public enum ApiKeyAuthenticationStatus
{
    Success,
    Unauthorized,
    Forbidden
}

public interface IApiKeyAuthenticator
{
    Task<ApiKeyAuthenticationOutcome> AuthenticateAsync(
        ApiKeyAuthenticationRequest request,
        CancellationToken cancellationToken);
}
