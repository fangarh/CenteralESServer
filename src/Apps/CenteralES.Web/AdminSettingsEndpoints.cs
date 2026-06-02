using CenteralES.AccessControl;
using CenteralES.PdfStampRecognition;
using CenteralES.Storage;

internal static class AdminSettingsEndpoints
{
    private static readonly HttpPdfStampRecognizerOptions DefaultHttpOptions = new();

    public static void MapAdminSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/settings", async (
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            IConfiguration configuration,
            AdminSettingsOptions settingsOptions,
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
            var endpointPool = processorSection.GetSection("endpointPool")
                .GetChildren()
                .Select(value => value.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => SanitizeEndpoint(value!))
                .ToArray();

            return Results.Ok(new AdminSettingsResponse(
                new AdminPublicApiSettingsResponse(settingsOptions.PdfMaxUploadBytes),
                new AdminStorageSettingsResponse(
                    settingsOptions.TemporaryRootPath,
                    settingsOptions.TemporaryStorageLimits.HardLimitBytes,
                    settingsOptions.TemporaryStorageLimits.SoftLimitBytes,
                    settingsOptions.TemporaryStorageLimits.MinimumFreeBytes),
                new AdminProcessorSettingsResponse(
                    PdfStampRecognitionConstants.ProcessorKey,
                    PdfStampRecognitionConstants.Capability,
                    ResolveString(configuration["PdfStampRecognition:Recognizer"], "Fake"),
                    endpointPool.Length,
                    endpointPool,
                    ResolvePositiveInt(processorSection["poolConcurrencyLimit"], DefaultHttpOptions.PoolConcurrencyLimit),
                    ResolvePositiveInt(processorSection["endpointConcurrencyLimit"], DefaultHttpOptions.EndpointConcurrencyLimit),
                    ResolveTimeSpan(processorSection["timeout"], DefaultHttpOptions.Timeout).ToString("c"),
                    ResolvePositiveInt(processorSection["maxAttempts"], 5),
                    ResolveTimeSpan(processorSection["processorOverloadedDelay"], TimeSpan.FromSeconds(15)).ToString("c"),
                    ResolveString(processorSection["contractVersion"], DefaultHttpOptions.ContractVersion)),
                new AdminSettingsBoundaryResponse(
                    ReadOnly: true,
                    EditingEnabled: false,
                    Note: "MVP shows safe runtime configuration only. Editing and sensitive values are intentionally not exposed.")));
        })
            .WithName("AdminGetSettings");
    }

    private static string SanitizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return "invalid-endpoint";
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
    }

    private static string ResolveString(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ResolvePositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static TimeSpan ResolveTimeSpan(string? value, TimeSpan fallback)
    {
        return TimeSpan.TryParse(value, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : fallback;
    }
}

internal sealed record AdminSettingsOptions(
    long PdfMaxUploadBytes,
    string TemporaryRootPath,
    TemporaryStorageLimits TemporaryStorageLimits);
