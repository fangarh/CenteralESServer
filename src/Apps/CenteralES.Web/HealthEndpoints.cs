using CenteralES.Storage;
using Npgsql;

internal static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () =>
            Results.Ok(new HealthResponse("healthy", DateTimeOffset.UtcNow)))
            .WithName("LiveHealth");

        app.MapGet("/health/ready", async (
            NpgsqlDataSource dataSource,
            ITemporaryFileStore temporaryFileStore,
            ITemporaryStorageMonitor temporaryStorageMonitor,
            CancellationToken cancellationToken) =>
        {
            var checkedAt = DateTimeOffset.UtcNow;
            var checks = new[]
            {
                await CheckPostgresAsync(dataSource, cancellationToken),
                await CheckProcessingSchemaAsync(dataSource, cancellationToken),
                await CheckTemporaryStorageAsync(temporaryFileStore, temporaryStorageMonitor, cancellationToken)
            };
            var status = checks.All(check => string.Equals(check.Status, "healthy", StringComparison.Ordinal))
                ? "healthy"
                : "unhealthy";

            return Results.Json(
                new ReadyHealthResponse(status, checkedAt, checks),
                statusCode: string.Equals(status, "healthy", StringComparison.Ordinal)
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status503ServiceUnavailable);
        })
            .WithName("ReadyHealth");
    }

    private static async Task<HealthCheckItemResponse> CheckPostgresAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("select 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return new HealthCheckItemResponse("postgres", "healthy");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckItemResponse("postgres", "unhealthy");
        }
    }

    private static async Task<HealthCheckItemResponse> CheckProcessingSchemaAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                select
                    to_regclass('public.processing_subjects') is not null
                    and to_regclass('public.processing_jobs') is not null
                    and to_regclass('public.processing_attempt_diagnostics') is not null
                    and to_regclass('public.processing_content_hashes') is not null
                    and to_regclass('public.processing_result_index') is not null
                    and to_regclass('public.pdf_stamp_recognition_results') is not null
                    and to_regclass('public.processing_worker_heartbeats') is not null
                    and to_regclass('public.client_applications') is not null
                    and to_regclass('public.admin_users') is not null
                    and to_regclass('public.admin_sessions') is not null
                    and to_regclass('public.admin_audit_events') is not null;
                """, connection);
            var compatible = await command.ExecuteScalarAsync(cancellationToken);
            return new HealthCheckItemResponse(
                "processingSchema",
                compatible is true ? "healthy" : "unhealthy");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckItemResponse("processingSchema", "unhealthy");
        }
    }

    private static async Task<HealthCheckItemResponse> CheckTemporaryStorageAsync(
        ITemporaryFileStore temporaryFileStore,
        ITemporaryStorageMonitor temporaryStorageMonitor,
        CancellationToken cancellationToken)
    {
        var key = $".health/ready-{Guid.NewGuid():N}.tmp";

        try
        {
            await using var content = new MemoryStream("ok"u8.ToArray());
            await temporaryFileStore.SaveAsync(key, content, cancellationToken);
            await using (var saved = await temporaryFileStore.OpenReadAsync(key, cancellationToken))
            {
                _ = saved.ReadByte();
            }

            await temporaryFileStore.DeleteIfExistsAsync(key, cancellationToken);
            var capacity = await temporaryStorageMonitor.CheckCapacityAsync(
                new TemporaryStorageCapacityRequest(0),
                cancellationToken);

            return new HealthCheckItemResponse(
                "temporaryStorage",
                capacity.Status is TemporaryStorageCapacityStatus.Full ? "unhealthy" : "healthy");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckItemResponse("temporaryStorage", "unhealthy");
        }
    }
}
