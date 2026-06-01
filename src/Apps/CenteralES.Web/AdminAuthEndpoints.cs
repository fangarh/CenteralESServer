using CenteralES.AccessControl;

internal static class AdminAuthEndpoints
{
    public static void MapAdminAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/auth/login", async (
            AdminLoginRequestBody login,
            HttpRequest request,
            HttpResponse response,
            IAdminAuthenticator adminAuthenticator,
            CancellationToken cancellationToken) =>
        {
            var outcome = await adminAuthenticator.LoginAsync(
                new AdminLoginRequest(
                    login.Login ?? string.Empty,
                    login.Password ?? string.Empty,
                    DateTimeOffset.UtcNow,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            if (outcome.Status is not AdminLoginStatus.Success
                || outcome.Principal is null
                || outcome.Credential is null)
            {
                return Results.Json(
                    ApiErrorResponse.Create("unauthorized", "Admin credentials are missing or invalid."),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            ApiAuthorization.AppendAdminSessionCookie(response, outcome.Credential.SessionToken, request.IsHttps);
            return Results.Ok(new AdminLoginResponse(
                ApiMappings.ToAdminUserResponse(outcome.Principal),
                outcome.Credential.CsrfToken,
                outcome.Credential.ExpiresAt,
                outcome.Credential.IdleExpiresAt));
        })
            .WithName("AdminLogin");

        app.MapGet("/api/admin/auth/me", async (
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizeAdminApiAsync(
                request,
                adminAuthenticator,
                requireCsrf: false,
                cancellationToken);
            if (authorization.Error is not null)
            {
                return authorization.Error;
            }

            return Results.Ok(new AdminMeResponse(ApiMappings.ToAdminUserResponse(authorization.Principal!)));
        })
            .WithName("AdminMe");

        app.MapPost("/api/admin/auth/logout", async (
            HttpRequest request,
            HttpResponse response,
            IAdminAuthenticator adminAuthenticator,
            CancellationToken cancellationToken) =>
        {
            var authorization = await ApiAuthorization.AuthorizeAdminApiAsync(
                request,
                adminAuthenticator,
                requireCsrf: true,
                cancellationToken);
            if (authorization.Error is not null)
            {
                return authorization.Error;
            }

            var sessionToken = ApiAuthorization.TryReadAdminSessionToken(request);
            await adminAuthenticator.LogoutAsync(sessionToken, DateTimeOffset.UtcNow, cancellationToken);
            ApiAuthorization.DeleteAdminSessionCookie(response, request.IsHttps);
            return Results.Ok(new AdminLogoutResponse(true));
        })
            .WithName("AdminLogout");
    }
}
