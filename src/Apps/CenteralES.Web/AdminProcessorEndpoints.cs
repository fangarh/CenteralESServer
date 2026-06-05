using CenteralES.AccessControl;
using CenteralES.Admin;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing;

internal static class AdminProcessorEndpoints
{
    public static void MapAdminProcessorEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/processors/{processorKey}", async (
            string processorKey,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessorReadStore readStore,
            IConfiguration configuration,
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

            if (!string.Equals(processorKey, PdfStampRecognitionConstants.ProcessorKey, StringComparison.Ordinal))
            {
                return Results.NotFound(ApiErrorResponse.Create("processor_not_found", $"Processor '{processorKey}' was not found."));
            }

            var status = await readStore.GetProcessorStatusAsync(
                PdfStampRecognitionConstants.ProcessorKey,
                PdfStampRecognitionConstants.Capability,
                recentDiagnosticsLimit: 10,
                cancellationToken);

            return Results.Ok(ApiMappings.ToAdminProcessorStatusResponse(
                status,
                AdminProcessorConfiguration.GetSanitizedEndpointPool(configuration)));
        })
            .WithName("AdminGetProcessorStatus");

        app.MapGet("/api/admin/processors/{processorKey}/endpoints", async (
            string processorKey,
            string? capability,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessorEndpointStore endpointStore,
            IConfiguration configuration,
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

            var processorError = ValidateProcessor(processorKey);
            if (processorError is not null)
            {
                return processorError;
            }

            var normalizedCapability = NormalizeCapability(capability);
            if (!string.Equals(normalizedCapability, PdfStampRecognitionConstants.Capability, StringComparison.Ordinal))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Unsupported processor capability."));
            }

            var dbEndpoints = await endpointStore.ListDbEndpointsAsync(
                PdfStampRecognitionConstants.ProcessorKey,
                PdfStampRecognitionConstants.Capability,
                cancellationToken);
            var envEndpoints = BuildEnvEndpointItems(configuration);
            var effective = ProcessorEndpointConfigurationMerger.MergeEnvAndDatabaseEndpoints(
                AdminProcessorConfiguration.GetEndpointPool(configuration),
                ResolveEndpointConcurrencyLimit(configuration),
                dbEndpoints.Select(endpoint => new ProcessorEndpointConfiguration(
                    endpoint.Endpoint,
                    endpoint.Enabled,
                    endpoint.ConcurrencyLimit,
                    endpoint.Source)).ToArray());

            return Results.Ok(new AdminProcessorEndpointListResponse(
                envEndpoints
                    .Concat(dbEndpoints)
                    .Select(ApiMappings.ToAdminProcessorEndpointResponse)
                    .ToArray(),
                effective.Select(ApiMappings.ToAdminProcessorEffectiveEndpointResponse).ToArray()));
        })
            .WithName("AdminListProcessorEndpoints");

        app.MapPost("/api/admin/processors/{processorKey}/endpoints", async (
            string processorKey,
            AdminCreateProcessorEndpointRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessorEndpointStore endpointStore,
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

            var processorError = ValidateProcessor(processorKey);
            if (processorError is not null)
            {
                return processorError;
            }

            var validationError = ValidateCreateRequest(body);
            if (validationError is not null)
            {
                return validationError;
            }

            var principal = authorization.Principal!;
            var result = await endpointStore.CreateAsync(
                new AdminCreateProcessorEndpointCommand(
                    PdfStampRecognitionConstants.ProcessorKey,
                    body.Capability!.Trim(),
                    body.Endpoint!.Trim(),
                    body.ConcurrencyLimit!.Value,
                    body.Priority ?? 0,
                    principal.UserId,
                    principal.Login,
                    DateTimeOffset.UtcNow,
                    body.Comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminCreateProcessorEndpointSuccess success => Results.Created(
                    $"/api/admin/processors/{processorKey}/endpoints/{success.Endpoint.Id:N}",
                    ApiMappings.ToAdminProcessorEndpointResponse(success.Endpoint)),
                AdminCreateProcessorEndpointConflict => Results.Json(
                    ApiErrorResponse.Create("processor_endpoint_conflict", "Processor endpoint already exists for this capability."),
                    statusCode: StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException($"Unknown processor endpoint creation result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminCreateProcessorEndpoint");

        app.MapPatch("/api/admin/processors/{processorKey}/endpoints/{endpointId:guid}", async (
            string processorKey,
            Guid endpointId,
            AdminUpdateProcessorEndpointRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessorEndpointStore endpointStore,
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

            var processorError = ValidateProcessor(processorKey);
            if (processorError is not null)
            {
                return processorError;
            }

            var validationError = ValidateUpdateRequest(body);
            if (validationError is not null)
            {
                return validationError;
            }

            var principal = authorization.Principal!;
            var result = await endpointStore.UpdateAsync(
                new AdminUpdateProcessorEndpointCommand(
                    endpointId,
                    PdfStampRecognitionConstants.ProcessorKey,
                    body.Enabled,
                    body.ConcurrencyLimit,
                    body.Priority,
                    principal.UserId,
                    principal.Login,
                    DateTimeOffset.UtcNow,
                    body.Comment,
                    request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    request.Headers.UserAgent.ToString()),
                cancellationToken);

            return result switch
            {
                AdminUpdateProcessorEndpointSuccess success => Results.Ok(ApiMappings.ToAdminProcessorEndpointResponse(success.Endpoint)),
                AdminUpdateProcessorEndpointNotFound => Results.NotFound(ApiErrorResponse.Create(
                    "processor_endpoint_not_found",
                    "Processor endpoint was not found.")),
                _ => throw new InvalidOperationException($"Unknown processor endpoint update result '{result.GetType().Name}'.")
            };
        })
            .WithName("AdminUpdateProcessorEndpoint");

        app.MapPost("/api/admin/processors/{processorKey}/endpoint-checks", async (
            string processorKey,
            AdminProcessorEndpointCheckRequestBody body,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessorEndpointStore endpointStore,
            IConfiguration configuration,
            PdfStampRecognitionEndpointChecker checker,
            AdminProcessorEndpointCheckLimiter limiter,
            AdminProcessorEndpointCheckPolicy policy,
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

            var processorError = ValidateProcessor(processorKey);
            if (processorError is not null)
            {
                return processorError;
            }

            if (!string.Equals(NormalizeCapability(body.Capability), PdfStampRecognitionConstants.Capability, StringComparison.Ordinal))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Unsupported processor capability."));
            }

            var target = await ResolveEndpointCheckTargetAsync(
                body,
                endpointStore,
                configuration,
                cancellationToken);
            if (target is null)
            {
                return Results.BadRequest(ApiErrorResponse.Create(
                    "invalid_input",
                    "Endpoint check target must reference a configured processor endpoint."));
            }

            var now = DateTimeOffset.UtcNow;
            if (!limiter.TryBegin(target.SanitizedEndpoint, now, policy.Cooldown, out var nextAllowedAt))
            {
                return Results.Json(
                    ApiErrorResponse.Create("processor_endpoint_check_cooldown", "Processor endpoint check cooldown is active."),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var result = await checker.CheckAsync(
                new PdfStampRecognitionEndpointCheckCommand(target.RawEndpoint, now),
                cancellationToken);

            return Results.Ok(ApiMappings.ToAdminProcessorEndpointCheckResponse(result, nextAllowedAt));
        })
            .WithName("AdminCheckProcessorEndpoint");
    }

    private static IResult? ValidateProcessor(string processorKey)
    {
        return string.Equals(processorKey, PdfStampRecognitionConstants.ProcessorKey, StringComparison.Ordinal)
            ? null
            : Results.NotFound(ApiErrorResponse.Create("processor_not_found", $"Processor '{processorKey}' was not found."));
    }

    private static IResult? ValidateCreateRequest(AdminCreateProcessorEndpointRequestBody body)
    {
        if (!string.Equals(NormalizeCapability(body.Capability), PdfStampRecognitionConstants.Capability, StringComparison.Ordinal))
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Unsupported processor capability."));
        }

        if (string.IsNullOrWhiteSpace(body.Endpoint) || !Uri.TryCreate(body.Endpoint.Trim(), UriKind.Absolute, out _))
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Endpoint must be an absolute URI."));
        }

        if (body.ConcurrencyLimit is null or <= 0 or > 1000)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Concurrency limit must be between 1 and 1000."));
        }

        if (body.Comment?.Length > 1000)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Comment must not exceed 1000 characters."));
        }

        return null;
    }

    private static IResult? ValidateUpdateRequest(AdminUpdateProcessorEndpointRequestBody body)
    {
        if (body.ConcurrencyLimit is <= 0 or > 1000)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Concurrency limit must be between 1 and 1000."));
        }

        if (body.Comment?.Length > 1000)
        {
            return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Comment must not exceed 1000 characters."));
        }

        return null;
    }

    private static IReadOnlyList<AdminProcessorEndpointListItem> BuildEnvEndpointItems(IConfiguration configuration)
    {
        var now = DateTimeOffset.UtcNow;
        return AdminProcessorConfiguration.GetEndpointPool(configuration)
            .Distinct(StringComparer.Ordinal)
            .Select(endpoint => new AdminProcessorEndpointListItem(
                Id: null,
                PdfStampRecognitionConstants.ProcessorKey,
                PdfStampRecognitionConstants.Capability,
                endpoint,
                Enabled: true,
                ResolveEndpointConcurrencyLimit(configuration),
                Priority: 0,
                Source: "env",
                CreatedAt: null,
                UpdatedAt: now,
                DisabledAt: null))
            .ToArray();
    }

    private static int ResolveEndpointConcurrencyLimit(IConfiguration configuration)
    {
        var configured = configuration["PdfStampRecognition:Processor:endpointConcurrencyLimit"];
        return int.TryParse(configured, out var value) && value > 0
            ? value
            : 1;
    }

    private static async Task<EndpointCheckTarget?> ResolveEndpointCheckTargetAsync(
        AdminProcessorEndpointCheckRequestBody body,
        IAdminProcessorEndpointStore endpointStore,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var dbEndpoints = await endpointStore.ListDbEndpointsAsync(
            PdfStampRecognitionConstants.ProcessorKey,
            PdfStampRecognitionConstants.Capability,
            cancellationToken);
        var candidates = BuildEnvEndpointItems(configuration)
            .Concat(dbEndpoints)
            .Select(endpoint => new EndpointCheckTarget(
                endpoint.Id,
                endpoint.Endpoint,
                AdminProcessorConfiguration.SanitizeEndpoint(endpoint.Endpoint)))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(body.EndpointId))
        {
            if (!Guid.TryParse(body.EndpointId, out var endpointId))
            {
                return null;
            }

            return candidates.FirstOrDefault(candidate => candidate.Id == endpointId);
        }

        if (string.IsNullOrWhiteSpace(body.Endpoint))
        {
            return null;
        }

        var sanitizedRequested = AdminProcessorConfiguration.SanitizeEndpoint(body.Endpoint.Trim());
        return candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.SanitizedEndpoint, sanitizedRequested, StringComparison.Ordinal));
    }

    private static string? NormalizeCapability(string? capability)
    {
        return string.IsNullOrWhiteSpace(capability)
            ? PdfStampRecognitionConstants.Capability
            : capability.Trim();
    }

    private sealed record EndpointCheckTarget(Guid? Id, string RawEndpoint, string SanitizedEndpoint);
}
