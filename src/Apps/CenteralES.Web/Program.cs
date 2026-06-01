using CenteralES.Admin;
using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Processing;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing.Queue;
using CenteralES.Storage;
using Npgsql;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddOpenApi();

var processingDatabaseConnectionString = ResolveProcessingDatabaseConnectionString(builder.Configuration, builder.Environment.ContentRootPath);
var temporaryStorageRoot = ResolveTemporaryStorageRoot(builder.Configuration);
var pdfMaxUploadBytes = ResolvePdfMaxUploadBytes(builder.Configuration);
var databaseBootstrapper = new PostgresDatabaseBootstrapper();
await databaseBootstrapper.EnsureDatabaseAsync(processingDatabaseConnectionString, CancellationToken.None);

builder.Services.AddSingleton(NpgsqlDataSource.Create(processingDatabaseConnectionString));
builder.Services.AddSingleton<IProcessingJobQueue, PostgresProcessingJobQueue>();
builder.Services.AddSingleton<IPdfStampRecognitionResultStore, PostgresPdfStampRecognitionResultStore>();
builder.Services.AddSingleton<ITemporaryFileStore>(_ => new LocalTemporaryFileStore(temporaryStorageRoot));
builder.Services.AddSingleton<IAdminProcessingReadStore, PostgresAdminProcessingReadStore>();

var app = builder.Build();

await databaseBootstrapper.ApplySchemaAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), CancellationToken.None);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health/live", () =>
    Results.Ok(new HealthResponse("healthy", DateTimeOffset.UtcNow)))
    .WithName("LiveHealth");

app.MapGet("/health/ready", async (
    NpgsqlDataSource dataSource,
    ITemporaryFileStore temporaryFileStore,
    CancellationToken cancellationToken) =>
{
    var checkedAt = DateTimeOffset.UtcNow;
    var checks = new[]
    {
        await CheckPostgresAsync(dataSource, cancellationToken),
        await CheckProcessingSchemaAsync(dataSource, cancellationToken),
        await CheckTemporaryStorageAsync(temporaryFileStore, cancellationToken)
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

app.MapPost("/api/pdf-stamp-recognition/jobs", async (
    HttpRequest request,
    IProcessingJobQueue queue,
    IPdfStampRecognitionResultStore resultStore,
    ITemporaryFileStore temporaryFileStore,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "PDF file must be sent as multipart form data."));
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "PDF file is required."));
    }

    if (file.Length > pdfMaxUploadBytes)
    {
        return Results.Json(
            ApiErrorResponse.Create("payload_too_large", $"PDF file exceeds the configured limit of {pdfMaxUploadBytes} bytes."),
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    await using var stream = file.OpenReadStream();
    var hash = await ContentHash.ComputeSha256Async(stream, cancellationToken);

    var existingResult = await resultStore.GetByHashAsync(hash, cancellationToken);
    if (existingResult is not null)
    {
        return Results.Ok(ToPdfResultResponse(existingResult));
    }

    var temporaryFileKey = $"incoming/{hash.Replace("sha256:", string.Empty, StringComparison.Ordinal)}.pdf";
    await using (var saveStream = file.OpenReadStream())
    {
        await temporaryFileStore.SaveAsync(temporaryFileKey, saveStream, cancellationToken);
    }

    var enqueueResult = await queue.EnqueueAsync(
        new CreateProcessingJobCommand(PdfStampRecognitionConstants.Capability, hash, temporaryFileKey, DateTimeOffset.UtcNow),
        cancellationToken);

    return Results.Accepted(
        $"/api/jobs/{enqueueResult.JobId:N}",
        new PdfJobResponse(
            hash,
            enqueueResult.JobId.ToString("N"),
            enqueueResult.AttemptNumber,
            ToPublicStatus(enqueueResult.Status),
            enqueueResult.Deduplicated));
})
.DisableAntiforgery()
.WithName("CreatePdfStampRecognitionJob");

app.MapGet("/api/pdf-stamp-recognition/results/{hash}", async (
    string hash,
    IProcessingJobQueue queue,
    IPdfStampRecognitionResultStore resultStore,
    CancellationToken cancellationToken) =>
{
    var result = await resultStore.GetByHashAsync(hash, cancellationToken);
    if (result is not null)
    {
        return Results.Ok(ToPdfResultResponse(result));
    }

    var currentJob = await queue.GetCurrentByHashAsync(PdfStampRecognitionConstants.Capability, hash, cancellationToken);
    return currentJob is not null && IsPublicPending(currentJob.Status)
        ? Results.Accepted($"/api/jobs/{currentJob.JobId:N}", ToPdfJobResponse(currentJob, deduplicated: true))
        : Results.NotFound(ApiErrorResponse.Create("result_not_found", $"No result or active processing found for hash '{hash}'."));
})
    .WithName("GetPdfStampRecognitionResult");

app.MapGet("/api/jobs/{jobId}", async (
    string jobId,
    IProcessingJobQueue queue,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(jobId, out var parsedJobId))
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
    }

    var job = await queue.GetJobAsync(parsedJobId, cancellationToken);
    return job is null
        ? Results.NotFound(ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found."))
        : Results.Ok(ToPdfJobResponse(job, deduplicated: false));
})
    .WithName("GetJob");

app.MapGet("/api/admin/jobs", async (
    string? capability,
    string? status,
    string? hash,
    int? limit,
    IAdminProcessingReadStore readStore,
    CancellationToken cancellationToken) =>
{
    if (!TryParseOptionalStatus(status, out var parsedStatus))
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job status '{status}' is not valid."));
    }

    var jobs = await readStore.ListJobsAsync(
        new AdminProcessingJobListQuery(
            string.IsNullOrWhiteSpace(capability) ? null : capability,
            parsedStatus,
            string.IsNullOrWhiteSpace(hash) ? null : hash,
            limit ?? 50),
        cancellationToken);

    return Results.Ok(new AdminJobListResponse(jobs.Select(ToAdminJobListItemResponse).ToArray()));
})
    .WithName("AdminListJobs");

app.MapGet("/api/admin/jobs/{jobId}", async (
    string jobId,
    IAdminProcessingReadStore readStore,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(jobId, out var parsedJobId))
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
    }

    var job = await readStore.GetJobAsync(parsedJobId, cancellationToken);
    return job is null
        ? Results.NotFound(ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found."))
        : Results.Ok(ToAdminJobDetailsResponse(job));
})
    .WithName("AdminGetJob");

app.MapGet("/api/admin/processors/{processorKey}", async (
    string processorKey,
    IAdminProcessingReadStore readStore,
    CancellationToken cancellationToken) =>
{
    if (!string.Equals(processorKey, PdfStampRecognitionConstants.ProcessorKey, StringComparison.Ordinal))
    {
        return Results.NotFound(ApiErrorResponse.Create("processor_not_found", $"Processor '{processorKey}' was not found."));
    }

    var status = await readStore.GetProcessorStatusAsync(
        PdfStampRecognitionConstants.ProcessorKey,
        PdfStampRecognitionConstants.Capability,
        recentDiagnosticsLimit: 10,
        cancellationToken);

    return Results.Ok(ToAdminProcessorStatusResponse(status));
})
    .WithName("AdminGetProcessorStatus");

app.Run();

static string ResolveProcessingDatabaseConnectionString(IConfiguration configuration, string contentRootPath)
{
    var configured = configuration.GetConnectionString("ProcessingDatabase");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    var fromEnvironment = Environment.GetEnvironmentVariable("CENTERALES_PROCESSING_DATABASE");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment;
    }

    var directory = new DirectoryInfo(contentRootPath);
    while (directory is not null)
    {
        var envPath = Path.Combine(directory.FullName, "db.env");
        if (File.Exists(envPath))
        {
            return PostgresConnectionString.ReadFromEnvFile(envPath, "test_db");
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Processing database connection string is not configured.");
}

static string ResolveTemporaryStorageRoot(IConfiguration configuration)
{
    var configured = configuration["Storage:TemporaryRoot"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    return Path.Combine(Path.GetTempPath(), "centerales-server", "temporary-files");
}

static long ResolvePdfMaxUploadBytes(IConfiguration configuration)
{
    const long defaultMaxUploadBytes = 250L * 1024 * 1024;
    const long hardMaxUploadBytes = 500L * 1024 * 1024;

    var configured = configuration["PdfStampRecognition:MaxUploadBytes"];
    if (string.IsNullOrWhiteSpace(configured))
    {
        return defaultMaxUploadBytes;
    }

    if (!long.TryParse(configured, out var value) || value <= 0)
    {
        throw new InvalidOperationException("Configuration value PdfStampRecognition:MaxUploadBytes must be a positive integer.");
    }

    if (value > hardMaxUploadBytes)
    {
        throw new InvalidOperationException($"Configuration value PdfStampRecognition:MaxUploadBytes must not exceed {hardMaxUploadBytes} bytes.");
    }

    return value;
}

static async Task<HealthCheckItemResponse> CheckPostgresAsync(
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

static async Task<HealthCheckItemResponse> CheckProcessingSchemaAsync(
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
                and to_regclass('public.processing_result_index') is not null
                and to_regclass('public.pdf_stamp_recognition_results') is not null
                and to_regclass('public.processing_worker_heartbeats') is not null;
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

static async Task<HealthCheckItemResponse> CheckTemporaryStorageAsync(
    ITemporaryFileStore temporaryFileStore,
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
        return new HealthCheckItemResponse("temporaryStorage", "healthy");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return new HealthCheckItemResponse("temporaryStorage", "unhealthy");
    }
}

static string ToPublicStatus(CenteralES.Processing.ProcessingJobStatus status)
{
    return status switch
    {
        CenteralES.Processing.ProcessingJobStatus.Queued => "queued",
        CenteralES.Processing.ProcessingJobStatus.Processing => "processing",
        CenteralES.Processing.ProcessingJobStatus.Completed => "completed",
        CenteralES.Processing.ProcessingJobStatus.Failed => "failed",
        CenteralES.Processing.ProcessingJobStatus.Blocked => "blocked",
        CenteralES.Processing.ProcessingJobStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown processing job status.")
    };
}

static PdfResultResponse ToPdfResultResponse(PdfStampRecognitionResult result)
{
    using var payload = JsonDocument.Parse(result.PayloadJson);

    return new PdfResultResponse(
        result.ContentHash,
        result.JobId.ToString("N"),
        "completed",
        result.ContractVersion,
        payload.RootElement.Clone());
}

static PdfJobResponse ToPdfJobResponse(ProcessingJobSnapshot job, bool deduplicated)
{
    return new PdfJobResponse(
        job.ContentHash,
        job.JobId.ToString("N"),
        job.AttemptNumber,
        ToPublicStatus(job.Status),
        deduplicated);
}

static bool IsPublicPending(CenteralES.Processing.ProcessingJobStatus status)
{
    return status is CenteralES.Processing.ProcessingJobStatus.Queued
        or CenteralES.Processing.ProcessingJobStatus.Processing;
}

static bool TryParseOptionalStatus(string? status, out CenteralES.Processing.ProcessingJobStatus? parsed)
{
    parsed = null;
    if (string.IsNullOrWhiteSpace(status))
    {
        return true;
    }

    parsed = status.ToLowerInvariant() switch
    {
        "queued" => CenteralES.Processing.ProcessingJobStatus.Queued,
        "processing" => CenteralES.Processing.ProcessingJobStatus.Processing,
        "completed" => CenteralES.Processing.ProcessingJobStatus.Completed,
        "failed" => CenteralES.Processing.ProcessingJobStatus.Failed,
        "blocked" => CenteralES.Processing.ProcessingJobStatus.Blocked,
        "cancelled" => CenteralES.Processing.ProcessingJobStatus.Cancelled,
        _ => null
    };

    return parsed is not null;
}

static AdminJobListItemResponse ToAdminJobListItemResponse(AdminProcessingJobListItem job)
{
    return new AdminJobListItemResponse(
        job.JobId.ToString("N"),
        job.SubjectId.ToString("N"),
        job.Capability,
        job.ContentHash,
        job.AttemptNumber,
        ToPublicStatus(job.Status),
        job.CreatedAt,
        job.StartedAt,
        job.FinishedAt,
        job.Endpoint,
        job.NormalizedError?.ToString(),
        job.Retryable,
        job.CorrelationId);
}

static AdminJobDetailsResponse ToAdminJobDetailsResponse(AdminProcessingJobDetails job)
{
    return new AdminJobDetailsResponse(
        job.JobId.ToString("N"),
        job.SubjectId.ToString("N"),
        job.Capability,
        job.ContentHash,
        job.TemporaryFileKey,
        job.AttemptNumber,
        ToPublicStatus(job.Status),
        job.ScheduledAt,
        job.StartedAt,
        job.FinishedAt,
        job.HeartbeatAt,
        job.CreatedAt,
        job.UpdatedAt,
        new AdminAttemptDiagnosticsResponse(
            job.Endpoint,
            job.Duration?.TotalMilliseconds,
            job.HttpStatus,
            job.NormalizedError?.ToString(),
            job.Retryable,
            job.RawErrorExcerpt,
            job.CorrelationId),
        job.Attempts.Select(ToAdminProcessingAttemptResponse).ToArray());
}

static AdminProcessingAttemptResponse ToAdminProcessingAttemptResponse(AdminProcessingAttemptDetails attempt)
{
    return new AdminProcessingAttemptResponse(
        attempt.JobId.ToString("N"),
        attempt.AttemptNumber,
        ToPublicStatus(attempt.Status),
        attempt.CreatedAt,
        attempt.StartedAt,
        attempt.FinishedAt,
        attempt.Endpoint,
        attempt.Duration?.TotalMilliseconds,
        attempt.HttpStatus,
        attempt.NormalizedError?.ToString(),
        attempt.Retryable,
        attempt.CorrelationId);
}

static AdminProcessorStatusResponse ToAdminProcessorStatusResponse(AdminProcessorStatus status)
{
    return new AdminProcessorStatusResponse(
        status.ProcessorKey,
        status.Capability,
        status.Health,
        new AdminProcessorQueueCountsResponse(
            status.Queue.Queued,
            status.Queue.Processing,
            status.Queue.Completed,
            status.Queue.Failed,
            status.Queue.Blocked,
            status.Queue.Cancelled),
        status.Workers.Select(ToAdminProcessorWorkerStatusResponse).ToArray(),
        status.RecentDiagnostics.Select(ToAdminProcessorRecentDiagnosticResponse).ToArray());
}

static AdminProcessorWorkerStatusResponse ToAdminProcessorWorkerStatusResponse(AdminProcessorWorkerStatus worker)
{
    return new AdminProcessorWorkerStatusResponse(
        worker.WorkerId,
        worker.StartedAt,
        worker.HeartbeatAt,
        worker.Stale);
}

static AdminProcessorRecentDiagnosticResponse ToAdminProcessorRecentDiagnosticResponse(AdminProcessorRecentDiagnostic diagnostic)
{
    return new AdminProcessorRecentDiagnosticResponse(
        diagnostic.JobId.ToString("N"),
        diagnostic.AttemptNumber,
        ToPublicStatus(diagnostic.Status),
        diagnostic.Endpoint,
        diagnostic.HttpStatus,
        diagnostic.NormalizedError?.ToString(),
        diagnostic.Retryable,
        diagnostic.CorrelationId,
        diagnostic.CreatedAt);
}

internal sealed record HealthResponse(string Status, DateTimeOffset CheckedAt);

internal sealed record ReadyHealthResponse(
    string Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<HealthCheckItemResponse> Checks);

internal sealed record HealthCheckItemResponse(
    string Name,
    string Status);

internal sealed record PdfJobResponse(
    string Hash,
    string JobId,
    int AttemptNumber,
    string Status,
    bool Deduplicated);

internal sealed record PdfResultResponse(
    string Hash,
    string JobId,
    string Status,
    string ContractVersion,
    JsonElement Result);

internal sealed record ApiErrorResponse(ApiError Error)
{
    public static ApiErrorResponse Create(string code, string message)
    {
        return new ApiErrorResponse(new ApiError(code, message, null, Guid.NewGuid().ToString("N")));
    }
}

internal sealed record ApiError(string Code, string Message, object? Details, string CorrelationId);

internal sealed record AdminJobListResponse(IReadOnlyList<AdminJobListItemResponse> Jobs);

internal sealed record AdminJobListItemResponse(
    string JobId,
    string SubjectId,
    string Capability,
    string Hash,
    int AttemptNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Endpoint,
    string? NormalizedError,
    bool? Retryable,
    string? CorrelationId);

internal sealed record AdminJobDetailsResponse(
    string JobId,
    string SubjectId,
    string Capability,
    string Hash,
    string TemporaryFileKey,
    int AttemptNumber,
    string Status,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? HeartbeatAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AdminAttemptDiagnosticsResponse Diagnostics,
    IReadOnlyList<AdminProcessingAttemptResponse> Attempts);

internal sealed record AdminAttemptDiagnosticsResponse(
    string? Endpoint,
    double? DurationMs,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string? RawErrorExcerpt,
    string? CorrelationId);

internal sealed record AdminProcessingAttemptResponse(
    string JobId,
    int AttemptNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Endpoint,
    double? DurationMs,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string? CorrelationId);

internal sealed record AdminProcessorStatusResponse(
    string ProcessorKey,
    string Capability,
    string Health,
    AdminProcessorQueueCountsResponse Queue,
    IReadOnlyList<AdminProcessorWorkerStatusResponse> Workers,
    IReadOnlyList<AdminProcessorRecentDiagnosticResponse> RecentDiagnostics);

internal sealed record AdminProcessorQueueCountsResponse(
    int Queued,
    int Processing,
    int Completed,
    int Failed,
    int Blocked,
    int Cancelled);

internal sealed record AdminProcessorWorkerStatusResponse(
    string WorkerId,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,
    bool Stale);

internal sealed record AdminProcessorRecentDiagnosticResponse(
    string JobId,
    int AttemptNumber,
    string Status,
    string? Endpoint,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string CorrelationId,
    DateTimeOffset CreatedAt);

public partial class Program;
