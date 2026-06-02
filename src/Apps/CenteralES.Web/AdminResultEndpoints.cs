using CenteralES.AccessControl;
using CenteralES.Admin;

internal static class AdminResultEndpoints
{
    public static void MapAdminResultEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/results", async (
            string? capability,
            string? hash,
            string? jobId,
            int? limit,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessingReadStore readStore,
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

            if (!TryParseOptionalGuid(jobId, out var parsedJobId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
            }

            var results = await readStore.ListResultsAsync(
                new AdminResultListQuery(
                    NormalizeFilter(capability),
                    NormalizeFilter(hash),
                    parsedJobId,
                    limit ?? 50),
                cancellationToken);

            return Results.Ok(new AdminResultListResponse(results.Select(ApiMappings.ToAdminResultResponse).ToArray()));
        })
            .WithName("AdminListResults");

        app.MapGet("/api/admin/results/{resultIndexId}", async (
            string resultIndexId,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessingReadStore readStore,
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

            if (!Guid.TryParse(resultIndexId, out var parsedResultIndexId))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Result id '{resultIndexId}' is not a valid GUID."));
            }

            var result = await readStore.GetResultAsync(parsedResultIndexId, cancellationToken);
            return result is null
                ? Results.NotFound(ApiErrorResponse.Create("result_not_found", $"Result '{resultIndexId}' was not found."))
                : Results.Ok(ApiMappings.ToAdminResultResponse(result));
        })
            .WithName("AdminGetResult");
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryParseOptionalGuid(string? value, out Guid? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Guid.TryParse(value, out var guid))
        {
            return false;
        }

        parsed = guid;
        return true;
    }
}
