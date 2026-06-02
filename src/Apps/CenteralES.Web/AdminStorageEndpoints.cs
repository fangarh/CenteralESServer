using CenteralES.AccessControl;
using CenteralES.Storage;

internal static class AdminStorageEndpoints
{
    public static void MapAdminStorageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/storage", async (
            HttpRequest request,
            IAdminAuthenticator adminAuthenticator,
            ITemporaryStorageMonitor temporaryStorageMonitor,
            AdminStorageOptions storageOptions,
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

            var capacity = await temporaryStorageMonitor.CheckCapacityAsync(
                new TemporaryStorageCapacityRequest(0),
                cancellationToken);

            return Results.Ok(new AdminStorageResponse(
                new AdminTemporaryStorageResponse(
                    "local",
                    "temporary-input",
                    storageOptions.TemporaryRootPath,
                    ToResponseStatus(capacity.Status),
                    capacity.UsedBytes,
                    capacity.HardLimitBytes,
                    capacity.SoftLimitBytes,
                    capacity.AvailableFreeBytes,
                    capacity.MinimumFreeBytes)));
        })
            .WithName("AdminGetStorage");
    }

    private static string ToResponseStatus(TemporaryStorageCapacityStatus status)
    {
        return status switch
        {
            TemporaryStorageCapacityStatus.Healthy => "healthy",
            TemporaryStorageCapacityStatus.Warning => "warning",
            TemporaryStorageCapacityStatus.Full => "full",
            _ => "unknown"
        };
    }
}

internal sealed record AdminStorageOptions(string TemporaryRootPath);
