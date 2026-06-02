using CenteralES.AccessControl;
using CenteralES.Admin;
using System.Globalization;

internal static class AdminAuditEndpoints
{
    public static void MapAdminAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/audit", async (
            string? action,
            string? targetType,
            string? targetId,
            string? actor,
            string? occurredFrom,
            string? occurredTo,
            int? limit,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminAuditReadStore readStore,
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

            if (!TryParseOptionalTimestamp(occurredFrom, out var parsedOccurredFrom))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "occurredFrom must be an ISO-8601 timestamp."));
            }

            if (!TryParseOptionalTimestamp(occurredTo, out var parsedOccurredTo))
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "occurredTo must be an ISO-8601 timestamp."));
            }

            if (parsedOccurredFrom is not null
                && parsedOccurredTo is not null
                && parsedOccurredFrom > parsedOccurredTo)
            {
                return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "occurredFrom must be earlier than occurredTo."));
            }

            var events = await readStore.ListAuditEventsAsync(
                new AdminAuditEventListQuery(
                    NormalizeFilter(action),
                    NormalizeFilter(targetType),
                    NormalizeFilter(targetId),
                    NormalizeFilter(actor),
                    parsedOccurredFrom,
                    parsedOccurredTo,
                    limit ?? 50),
                cancellationToken);

            return Results.Ok(new AdminAuditListResponse(events.Select(ApiMappings.ToAdminAuditEventResponse).ToArray()));
        })
            .WithName("AdminListAuditEvents");
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryParseOptionalTimestamp(string? value, out DateTimeOffset? timestamp)
    {
        timestamp = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return false;
        }

        timestamp = parsed;
        return true;
    }
}
