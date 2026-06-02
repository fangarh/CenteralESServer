using CenteralES.AccessControl;
using CenteralES.Admin;

internal static class AdminApiKeyEndpoints
{
    public static void MapAdminApiKeyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/api-keys", async (
            string? keyId,
            bool? active,
            int? limit,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminApiKeyStore apiKeyStore,
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

            var keys = await apiKeyStore.ListAsync(
                new AdminApiKeyListQuery(NormalizeFilter(keyId), active, limit ?? 50),
                cancellationToken);

            return Results.Ok(new AdminApiKeyListResponse(keys.Select(ApiMappings.ToAdminApiKeyResponse).ToArray()));
        })
            .WithName("AdminListApiKeys");

        app.MapPost("/api/admin/api-keys", async (
            AdminCreateApiKeyRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminApiKeyStore apiKeyStore,
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
            var now = DateTimeOffset.UtcNow;
            var result = await apiKeyStore.CreateAsync(
                new AdminCreateApiKeyCommand(
                    body.KeyId!.Trim(),
                    body.Name!.Trim(),
                    NormalizeCapabilities(body.AllowedCapabilities!),
                    body.ExpiresAt,
                    principal.UserId,
                    principal.Login,
                    now,
                    body.Comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminCreateApiKeySuccess success => Results.Created(
                    $"/api/admin/api-keys/{Uri.EscapeDataString(success.Key.KeyId)}",
                    ApiMappings.ToAdminCreateApiKeyResponse(success)),
                AdminCreateApiKeyConflict => Results.Json(
                    ApiErrorResponse.Create("api_key_conflict", "API key id already exists."),
                    statusCode: StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException($"Unknown API key creation result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminCreateApiKey");

        app.MapPost("/api/admin/api-keys/{keyId}/disable", async (
            string keyId,
            AdminDisableApiKeyRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminApiKeyStore apiKeyStore,
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

            var normalizedKeyId = NormalizeFilter(keyId);
            if (!IsValidKeyId(normalizedKeyId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "API key id is invalid."));
            }

            if (body.Comment?.Length > 1000)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Comment must not exceed 1000 characters."));
            }

            var principal = authorization.Principal!;
            var result = await apiKeyStore.DisableAsync(
                new AdminDisableApiKeyCommand(
                    normalizedKeyId!,
                    principal.UserId,
                    principal.Login,
                    DateTimeOffset.UtcNow,
                    body.Comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminDisableApiKeySuccess success => Results.Ok(ApiMappings.ToAdminDisableApiKeyResponse(success)),
                AdminDisableApiKeyNotFound => Results.NotFound(
                    ApiErrorResponse.Create("api_key_not_found", $"API key '{keyId}' was not found.")),
                AdminDisableApiKeyConflict => Results.Json(
                    ApiErrorResponse.Create("api_key_already_disabled", "API key is already disabled."),
                    statusCode: StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException($"Unknown API key disable result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminDisableApiKey");
    }

    private static IResult? ValidateCreateRequest(AdminCreateApiKeyRequestBody body)
    {
        if (!IsValidKeyId(NormalizeFilter(body.KeyId)))
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "API key id must be 3-80 characters and contain only letters, digits, '.', '_' or '-'."));
        }

        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Trim().Length > 200)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "API key name is required and must not exceed 200 characters."));
        }

        if (NormalizeCapabilities(body.AllowedCapabilities ?? Array.Empty<string>()).Count == 0)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "At least one allowed capability is required."));
        }

        if (body.ExpiresAt is not null && body.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "API key expiration must be in the future."));
        }

        if (body.Comment?.Length > 1000)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Comment must not exceed 1000 characters."));
        }

        return null;
    }

    private static bool IsValidKeyId(string? keyId)
    {
        return keyId is { Length: >= 3 and <= 80 }
            && keyId.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');
    }

    private static IReadOnlyList<string> NormalizeCapabilities(IReadOnlyList<string> capabilities)
    {
        return capabilities
            .Select(NormalizeFilter)
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.Ordinal)
            .ToArray()!;
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
