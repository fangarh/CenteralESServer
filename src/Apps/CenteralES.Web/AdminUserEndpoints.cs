using CenteralES.AccessControl;
using CenteralES.Admin;

internal static class AdminUserEndpoints
{
    public static void MapAdminUserEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/users", async (
            string? login,
            bool? active,
            int? limit,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminUserStore userStore,
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

            var users = await userStore.ListAsync(
                new AdminUserListQuery(NormalizeFilter(login), active, limit ?? 50),
                cancellationToken);

            return Results.Ok(new AdminUserListResponse(users.Select(ApiMappings.ToAdminManagedUserResponse).ToArray()));
        })
            .WithName("AdminListUsers");

        app.MapPost("/api/admin/users", async (
            AdminCreateUserRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminUserStore userStore,
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

            var validationError = ValidateCreateRequest(body);
            if (validationError is not null)
            {
                return validationError;
            }

            var principal = authorization.Principal!;
            var result = await userStore.CreateAsync(
                new AdminCreateUserCommand(
                    body.Login!.Trim(),
                    body.Password!,
                    principal.UserId,
                    principal.Login,
                    DateTimeOffset.UtcNow,
                    body.Comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminCreateUserSuccess success => Results.Created(
                    $"/api/admin/users/{success.User.UserId:N}",
                    ApiMappings.ToAdminCreateUserResponse(success)),
                AdminCreateUserConflict => Results.Json(
                    ApiErrorResponse.Create("admin_user_conflict", "Admin user login already exists."),
                    statusCode: StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException($"Unknown admin user creation result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminCreateUser");

        app.MapPost("/api/admin/users/{userId}/disable", async (
            string userId,
            AdminDisableUserRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminUserStore userStore,
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

            if (!Guid.TryParse(userId, out var parsedUserId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Admin user id '{userId}' is not a valid GUID."));
            }

            if (body.Comment?.Length > 1000)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Comment must not exceed 1000 characters."));
            }

            var principal = authorization.Principal!;
            var result = await userStore.DisableAsync(
                new AdminDisableUserCommand(
                    parsedUserId,
                    principal.UserId,
                    principal.Login,
                    DateTimeOffset.UtcNow,
                    body.Comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminDisableUserSuccess success => Results.Ok(ApiMappings.ToAdminDisableUserResponse(success)),
                AdminDisableUserNotFound => Results.NotFound(
                    ApiErrorResponse.Create("admin_user_not_found", $"Admin user '{userId}' was not found.")),
                AdminDisableUserConflict => Results.Json(
                    ApiErrorResponse.Create("admin_user_already_disabled", "Admin user is already disabled."),
                    statusCode: StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException($"Unknown admin user disable result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminDisableUser");

        app.MapPost("/api/admin/users/{userId}/password", async (
            string userId,
            AdminChangeUserPasswordRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminUserStore userStore,
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

            if (!Guid.TryParse(userId, out var parsedUserId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Admin user id '{userId}' is not a valid GUID."));
            }

            if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < AdminPasswordHasher.MinimumPasswordLength)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Password must be at least {AdminPasswordHasher.MinimumPasswordLength} characters."));
            }

            if (body.Comment?.Length > 1000)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Comment must not exceed 1000 characters."));
            }

            var principal = authorization.Principal!;
            var result = await userStore.ChangePasswordAsync(
                new AdminChangeUserPasswordCommand(
                    parsedUserId,
                    body.Password,
                    principal.UserId,
                    principal.Login,
                    DateTimeOffset.UtcNow,
                    body.Comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminChangeUserPasswordSuccess success => Results.Ok(ApiMappings.ToAdminChangeUserPasswordResponse(success)),
                AdminChangeUserPasswordNotFound => Results.NotFound(
                    ApiErrorResponse.Create("admin_user_not_found", $"Admin user '{userId}' was not found.")),
                AdminChangeUserPasswordConflict => Results.Json(
                    ApiErrorResponse.Create("admin_user_disabled", "Password cannot be changed for a disabled admin user."),
                    statusCode: StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException($"Unknown admin user password change result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminChangeUserPassword");
    }

    private static IResult? ValidateCreateRequest(AdminCreateUserRequestBody body)
    {
        var login = NormalizeFilter(body.Login);
        if (login is null || login.Length is < 3 or > 80)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Admin login must be 3-80 characters."));
        }

        if (!login.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Admin login can contain only letters, digits, '.', '_' or '-'."));
        }

        if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < AdminPasswordHasher.MinimumPasswordLength)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Password must be at least {AdminPasswordHasher.MinimumPasswordLength} characters."));
        }

        if (body.Comment?.Length > 1000)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Comment must not exceed 1000 characters."));
        }

        return null;
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
