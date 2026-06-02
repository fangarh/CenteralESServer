using CenteralES.AccessControl;
using CenteralES.Admin;
using CenteralES.PdfStampRecognition;

internal static class AdminProcessorEndpoints
{
    public static void MapAdminProcessorEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/processors/{processorKey}", async (
            string processorKey,
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IAdminProcessorReadStore readStore,
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

            return Results.Ok(ApiMappings.ToAdminProcessorStatusResponse(status));
        })
            .WithName("AdminGetProcessorStatus");
    }
}
