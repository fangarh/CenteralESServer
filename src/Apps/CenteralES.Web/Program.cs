using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Processing;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing.Queue;
using CenteralES.Storage;
using Npgsql;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var processingDatabaseConnectionString = ResolveProcessingDatabaseConnectionString(builder.Configuration, builder.Environment.ContentRootPath);
var databaseBootstrapper = new PostgresDatabaseBootstrapper();
await databaseBootstrapper.EnsureDatabaseAsync(processingDatabaseConnectionString, CancellationToken.None);

builder.Services.AddSingleton(NpgsqlDataSource.Create(processingDatabaseConnectionString));
builder.Services.AddSingleton<IProcessingJobQueue, PostgresProcessingJobQueue>();
builder.Services.AddSingleton<IPdfStampRecognitionResultStore, PostgresPdfStampRecognitionResultStore>();

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

app.MapGet("/health/ready", () =>
    Results.Ok(new HealthResponse("healthy", DateTimeOffset.UtcNow)))
    .WithName("ReadyHealth");

app.MapPost("/api/pdf-stamp-recognition/jobs", async (
    HttpRequest request,
    IProcessingJobQueue queue,
    IPdfStampRecognitionResultStore resultStore,
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

    await using var stream = file.OpenReadStream();
    var hash = await ContentHash.ComputeSha256Async(stream, cancellationToken);

    var existingResult = await resultStore.GetByHashAsync(hash, cancellationToken);
    if (existingResult is not null)
    {
        return Results.Ok(ToPdfResultResponse(existingResult));
    }

    var temporaryFileKey = $"incoming/{hash.Replace("sha256:", string.Empty, StringComparison.Ordinal)}.pdf";
    var enqueueResult = await queue.EnqueueAsync(
        new CreateProcessingJobCommand("pdf-stamp-recognition", hash, temporaryFileKey, DateTimeOffset.UtcNow),
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

internal sealed record HealthResponse(string Status, DateTimeOffset CheckedAt);

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

public partial class Program;
