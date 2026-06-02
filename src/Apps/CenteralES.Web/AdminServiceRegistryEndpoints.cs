using CenteralES.AccessControl;
using CenteralES.PdfStampRecognition;

internal static class AdminServiceRegistryEndpoints
{
    private static readonly HttpPdfStampRecognizerOptions DefaultHttpOptions = new();

    public static void MapAdminServiceRegistryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/services", async (
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
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

            var processorSection = configuration.GetSection("PdfStampRecognition:Processor");
            var endpointCount = processorSection.GetSection("endpointPool")
                .GetChildren()
                .Count(value => !string.IsNullOrWhiteSpace(value.Value));

            return Results.Ok(new AdminServiceRegistryResponse(
            [
                new AdminRegisteredServiceResponse(
                    PdfStampRecognitionConstants.Capability,
                    PdfStampRecognitionConstants.ProcessorKey,
                    "PDF stamp recognition",
                    "Asynchronous PDF stamp recognition through the configured pdf2txt processor.",
                    Enabled: true,
                    ResolveString(configuration["PdfStampRecognition:Recognizer"], "Fake"),
                    endpointCount,
                    ResolveString(processorSection["contractVersion"], DefaultHttpOptions.ContractVersion),
                    "passive",
                    $"/api/admin/processors/{PdfStampRecognitionConstants.ProcessorKey}",
                    "/api/admin/settings",
                    ["health", "passive-processor-status", "public-pdf-upload"],
                    [
                        new AdminServicePublicEndpointResponse(
                            "POST",
                            "/api/pdf-stamp-recognition/jobs",
                            "Submit a PDF for asynchronous recognition."),
                        new AdminServicePublicEndpointResponse(
                            "GET",
                            "/api/pdf-stamp-recognition/results/{hash}",
                            "Poll or read the recognition result by content hash."),
                        new AdminServicePublicEndpointResponse(
                            "GET",
                            "/api/jobs/{jobId}",
                            "Read sanitized processing job status.")
                    ])
            ]));
        })
            .WithName("AdminGetServices");
    }

    private static string ResolveString(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
