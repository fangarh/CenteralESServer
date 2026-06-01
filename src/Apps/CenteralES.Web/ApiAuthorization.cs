using CenteralES.AccessControl;

internal static class ApiAuthorization
{
    public static async Task<IResult?> AuthorizePublicApiAsync(
        HttpRequest request,
        IApiKeyAuthenticator authenticator,
        string requiredCapability,
        CancellationToken cancellationToken)
    {
        var credential = TryParseApiKeyCredential(request.Headers.Authorization.ToString());
        if (credential is null)
        {
            return Results.Json(
                ApiErrorResponse.Create("unauthorized", "API key is missing or invalid."),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var outcome = await authenticator.AuthenticateAsync(
            new ApiKeyAuthenticationRequest(
                credential.Value.KeyId,
                credential.Value.Secret,
                requiredCapability,
                DateTimeOffset.UtcNow,
                request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                request.Headers.UserAgent.ToString()),
            cancellationToken);

        return outcome.Status switch
        {
            ApiKeyAuthenticationStatus.Success => null,
            ApiKeyAuthenticationStatus.Forbidden => Results.Json(
                ApiErrorResponse.Create("forbidden", "API key is not allowed to use the requested capability."),
                statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Json(
                ApiErrorResponse.Create("unauthorized", "API key is missing or invalid."),
                statusCode: StatusCodes.Status401Unauthorized)
        };
    }

    public static async Task<AdminAuthorizationResult> AuthorizeAdminApiAsync(
        HttpRequest request,
        IAdminAuthenticator adminAuthenticator,
        bool requireCsrf,
        CancellationToken cancellationToken)
    {
        var outcome = await adminAuthenticator.ValidateSessionAsync(
            new AdminSessionValidationRequest(
                TryReadAdminSessionToken(request),
                request.Headers["X-CSRF-Token"].ToString(),
                requireCsrf,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return outcome.Status switch
        {
            AdminSessionValidationStatus.Success => new AdminAuthorizationResult(null, outcome.Principal),
            AdminSessionValidationStatus.Forbidden => new AdminAuthorizationResult(
                Results.Json(
                    ApiErrorResponse.Create("forbidden", "CSRF token is missing or invalid."),
                    statusCode: StatusCodes.Status403Forbidden),
                null),
            _ => new AdminAuthorizationResult(
                Results.Json(
                    ApiErrorResponse.Create("unauthorized", "Admin session is missing or invalid."),
                    statusCode: StatusCodes.Status401Unauthorized),
                null)
        };
    }

    public static string? TryReadAdminSessionToken(HttpRequest request)
    {
        return request.Cookies.TryGetValue("centerales_admin_session", out var value)
            ? value
            : null;
    }

    public static void AppendAdminSessionCookie(HttpResponse response, string sessionToken, bool secure)
    {
        response.Cookies.Append(
            "centerales_admin_session",
            sessionToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Strict,
                Path = "/api/admin",
                MaxAge = TimeSpan.FromHours(24)
            });
    }

    public static void DeleteAdminSessionCookie(HttpResponse response, bool secure)
    {
        response.Cookies.Delete(
            "centerales_admin_session",
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Strict,
                Path = "/api/admin"
            });
    }

    private static ApiKeyCredential? TryParseApiKeyCredential(string authorization)
    {
        const string scheme = "ApiKey ";
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith(scheme, StringComparison.Ordinal))
        {
            return null;
        }

        var value = authorization[scheme.Length..].Trim();
        var separator = value.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return null;
        }

        var keyId = value[..separator];
        var secret = value[(separator + 1)..];
        return string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret)
            ? null
            : new ApiKeyCredential(keyId, secret);
    }
}
