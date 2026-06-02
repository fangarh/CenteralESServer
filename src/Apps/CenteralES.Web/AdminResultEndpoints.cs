using CenteralES.AccessControl;
using CenteralES.Admin;
using System.Text.Json.Nodes;

internal static class AdminResultEndpoints
{
    private const string PdfStampRecognitionPayloadTable = "pdf_stamp_recognition_results";
    private const long MaxDebugPayloadBytes = 1L * 1024 * 1024;

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
                : Results.Ok(ApiMappings.ToAdminResultDetailsResponse(result));
        })
            .WithName("AdminGetResult");

        app.MapGet("/api/admin/results/{resultIndexId}/payload", async (
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
            if (result is null)
            {
                return Results.NotFound(ApiErrorResponse.Create("result_not_found", $"Result '{resultIndexId}' was not found."));
            }

            if (!string.Equals(result.Reference.PayloadTable, PdfStampRecognitionPayloadTable, StringComparison.Ordinal))
            {
                return Results.Json(
                    ApiErrorResponse.Create(
                        "unsupported_payload_table",
                        $"Payload table '{result.Reference.PayloadTable}' is not available through this debug endpoint."),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (result.Reference.PayloadSize > MaxDebugPayloadBytes)
            {
                return Results.Json(
                    ApiErrorResponse.Create(
                        "payload_too_large",
                        $"Payload size {result.Reference.PayloadSize} bytes exceeds the debug endpoint limit of {MaxDebugPayloadBytes} bytes."),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var payload = await readStore.GetPdfStampRecognitionPayloadAsync(
                result.Reference.PayloadId,
                cancellationToken);
            if (payload is null)
            {
                return Results.NotFound(ApiErrorResponse.Create(
                    "payload_not_found",
                    $"Payload '{result.Reference.PayloadId:N}' was not found."));
            }

            var jsonPayload = JsonNode.Parse(payload.PayloadJson)
                ?? throw new InvalidOperationException("Stored payload JSON could not be parsed.");

            return Results.Ok(new AdminResultPayloadResponse(
                result.Reference.ResultIndexId.ToString("N"),
                result.Reference.PayloadTable,
                payload.PayloadId.ToString("N"),
                result.Reference.ContractVersion,
                result.Reference.PayloadSize,
                "Debug endpoint: raw processor JSON is returned only to authenticated admins. Do not expose it in regular Admin UI summaries.",
                jsonPayload));
        })
            .WithName("AdminGetResultPayload");
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
