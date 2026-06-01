using CenteralES.AccessControl;
using CenteralES.Admin;
using CenteralES.Infrastructure.AccessControl;
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
var temporaryStorageLimits = ResolveTemporaryStorageLimits(builder.Configuration);
var pdfMaxUploadBytes = ResolvePdfMaxUploadBytes(builder.Configuration);
var databaseBootstrapper = new PostgresDatabaseBootstrapper();
await databaseBootstrapper.EnsureDatabaseAsync(processingDatabaseConnectionString, CancellationToken.None);

builder.Services.AddSingleton(NpgsqlDataSource.Create(processingDatabaseConnectionString));
builder.Services.AddSingleton<IApiKeyAuthenticator, PostgresApiKeyAuthenticator>();
builder.Services.AddSingleton<IAdminAuthenticator, PostgresAdminAuthenticator>();
builder.Services.AddSingleton<IProcessingJobQueue, PostgresProcessingJobQueue>();
builder.Services.AddSingleton<IPdfStampRecognitionResultStore, PostgresPdfStampRecognitionResultStore>();
builder.Services.AddSingleton<ITemporaryFileStore>(_ => new LocalTemporaryFileStore(temporaryStorageRoot));
builder.Services.AddSingleton<ITemporaryStorageMonitor>(_ => new LocalTemporaryStorageMonitor(temporaryStorageRoot, temporaryStorageLimits));
builder.Services.AddSingleton<IAdminProcessingReadStore, PostgresAdminProcessingReadStore>();
builder.Services.AddSingleton<IAdminProcessingActionStore, PostgresAdminProcessingActionStore>();

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

app.MapPost("/api/pdf-stamp-recognition/jobs", async (
    HttpRequest request,
    IApiKeyAuthenticator apiKeyAuthenticator,
    IProcessingJobQueue queue,
    IPdfStampRecognitionResultStore resultStore,
    ITemporaryFileStore temporaryFileStore,
    ITemporaryStorageMonitor temporaryStorageMonitor,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizePublicApiAsync(
        request,
        apiKeyAuthenticator,
        PdfStampRecognitionConstants.Capability,
        cancellationToken);
    if (authorization is not null)
    {
        return authorization;
    }

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
    var capacity = await temporaryStorageMonitor.CheckCapacityAsync(
        new TemporaryStorageCapacityRequest(file.Length),
        cancellationToken);
    if (capacity.Status is TemporaryStorageCapacityStatus.Full)
    {
        return Results.Json(
            ApiErrorResponse.Create("temporary_storage_full", "Temporary storage is full. Try again after cleanup frees space."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

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
    HttpRequest request,
    IApiKeyAuthenticator apiKeyAuthenticator,
    IProcessingJobQueue queue,
    IPdfStampRecognitionResultStore resultStore,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizePublicApiAsync(
        request,
        apiKeyAuthenticator,
        PdfStampRecognitionConstants.Capability,
        cancellationToken);
    if (authorization is not null)
    {
        return authorization;
    }

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
    HttpRequest request,
    IApiKeyAuthenticator apiKeyAuthenticator,
    IProcessingJobQueue queue,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizePublicApiAsync(
        request,
        apiKeyAuthenticator,
        PdfStampRecognitionConstants.Capability,
        cancellationToken);
    if (authorization is not null)
    {
        return authorization;
    }

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

app.MapPost("/api/admin/auth/login", async (
    AdminLoginRequestBody login,
    HttpRequest request,
    HttpResponse response,
    IAdminAuthenticator adminAuthenticator,
    CancellationToken cancellationToken) =>
{
    var outcome = await adminAuthenticator.LoginAsync(
        new AdminLoginRequest(
            login.Login ?? string.Empty,
            login.Password ?? string.Empty,
            DateTimeOffset.UtcNow,
            request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            request.Headers.UserAgent.ToString()),
        cancellationToken);

    if (outcome.Status is not AdminLoginStatus.Success
        || outcome.Principal is null
        || outcome.Credential is null)
    {
        return Results.Json(
            ApiErrorResponse.Create("unauthorized", "Admin credentials are missing or invalid."),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    AppendAdminSessionCookie(response, outcome.Credential.SessionToken, request.IsHttps);
    return Results.Ok(new AdminLoginResponse(
        ToAdminUserResponse(outcome.Principal),
        outcome.Credential.CsrfToken,
        outcome.Credential.ExpiresAt,
        outcome.Credential.IdleExpiresAt));
})
    .WithName("AdminLogin");

app.MapGet("/api/admin/auth/me", async (
    HttpRequest request,
    IAdminAuthenticator adminAuthenticator,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizeAdminApiAsync(
        request,
        adminAuthenticator,
        requireCsrf: false,
        cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

    return Results.Ok(new AdminMeResponse(ToAdminUserResponse(authorization.Principal!)));
})
    .WithName("AdminMe");

app.MapPost("/api/admin/auth/logout", async (
    HttpRequest request,
    HttpResponse response,
    IAdminAuthenticator adminAuthenticator,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizeAdminApiAsync(
        request,
        adminAuthenticator,
        requireCsrf: true,
        cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

    var sessionToken = TryReadAdminSessionToken(request);
    await adminAuthenticator.LogoutAsync(sessionToken, DateTimeOffset.UtcNow, cancellationToken);
    DeleteAdminSessionCookie(response, request.IsHttps);
    return Results.Ok(new AdminLogoutResponse(true));
})
    .WithName("AdminLogout");

app.MapGet("/api/admin/jobs", async (
    string? capability,
    string? status,
    string? hash,
    int? limit,
    HttpRequest request,
    IAdminAuthenticator adminAuthenticator,
    IAdminProcessingReadStore readStore,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizeAdminApiAsync(
        request,
        adminAuthenticator,
        requireCsrf: false,
        cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

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
    HttpRequest request,
    IAdminAuthenticator adminAuthenticator,
    IAdminProcessingReadStore readStore,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizeAdminApiAsync(
        request,
        adminAuthenticator,
        requireCsrf: false,
        cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

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

app.MapGet("/api/admin/jobs/{jobId}/support-report", async (
    string jobId,
    HttpRequest request,
    IAdminAuthenticator adminAuthenticator,
    IAdminProcessingReadStore readStore,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizeAdminApiAsync(
        request,
        adminAuthenticator,
        requireCsrf: false,
        cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

    if (!Guid.TryParse(jobId, out var parsedJobId))
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
    }

    var report = await readStore.GetJobSupportReportAsync(
        parsedJobId,
        PdfStampRecognitionConstants.ProcessorKey,
        cancellationToken);
    return report is null
        ? Results.NotFound(ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found."))
        : Results.Ok(ToAdminJobSupportReportResponse(report));
})
    .WithName("AdminGetJobSupportReport");

app.MapPost("/api/admin/jobs/{jobId}/retry", async (
    string jobId,
    AdminManualRetryRequestBody retry,
    HttpRequest request,
    IAdminAuthenticator adminAuthenticator,
    IAdminProcessingActionStore actionStore,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizeAdminApiAsync(
        request,
        adminAuthenticator,
        requireCsrf: true,
        cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

    if (!Guid.TryParse(jobId, out var parsedJobId))
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", $"Job id '{jobId}' is not a valid GUID."));
    }

    var comment = retry.Comment?.Trim();
    if (comment?.Length > 1000)
    {
        return Results.BadRequest(ApiErrorResponse.Create("invalid_input", "Retry comment must not exceed 1000 characters."));
    }

    var principal = authorization.Principal!;
    var result = await actionStore.ManualRetryJobAsync(
        new AdminManualRetryJobCommand(
            parsedJobId,
            principal.UserId,
            principal.Login,
            DateTimeOffset.UtcNow,
            comment,
            request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            request.Headers.UserAgent.ToString()),
        cancellationToken);

    return result.Status switch
    {
        AdminManualRetryJobStatus.Success => Results.Accepted(
            $"/api/jobs/{result.NewJobId:N}",
            new AdminManualRetryResponse(
                result.SourceJobId!.Value.ToString("N"),
                result.NewJobId!.Value.ToString("N"),
                result.ContentHash!,
                result.AttemptNumber!.Value,
                ToPublicStatus(result.NewStatus!.Value),
                result.AuditId!.Value.ToString("N"))),
        AdminManualRetryJobStatus.NotFound => Results.NotFound(
            ApiErrorResponse.Create("job_not_found", $"Job '{jobId}' was not found.")),
        _ => Results.Json(
            ApiErrorResponse.Create("retry_not_allowed", "Manual retry is allowed only for the current failed or blocked job."),
            statusCode: StatusCodes.Status409Conflict)
    };
})
    .WithName("AdminManualRetryJob");

app.MapGet("/api/admin/processors/{processorKey}", async (
    string processorKey,
    HttpRequest request,
    IAdminAuthenticator adminAuthenticator,
    IAdminProcessingReadStore readStore,
    CancellationToken cancellationToken) =>
{
    var authorization = await AuthorizeAdminApiAsync(
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

static TemporaryStorageLimits ResolveTemporaryStorageLimits(IConfiguration configuration)
{
    var limits = new TemporaryStorageLimits(
        HardLimitBytes: ResolveOptionalPositiveLong(configuration, "Storage:TemporaryHardLimitBytes"),
        SoftLimitBytes: ResolveOptionalPositiveLong(configuration, "Storage:TemporarySoftLimitBytes"),
        MinimumFreeBytes: ResolveOptionalPositiveLong(configuration, "Storage:TemporaryMinimumFreeBytes"));
    limits.Validate();
    return limits;
}

static long? ResolveOptionalPositiveLong(IConfiguration configuration, string key)
{
    var configured = configuration[key];
    if (string.IsNullOrWhiteSpace(configured))
    {
        return null;
    }

    if (!long.TryParse(configured, out var value) || value <= 0)
    {
        throw new InvalidOperationException($"Configuration value {key} must be a positive integer.");
    }

    return value;
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

static async Task<HealthCheckItemResponse> CheckTemporaryStorageAsync(
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

static async Task<IResult?> AuthorizePublicApiAsync(
    HttpRequest request,
    IApiKeyAuthenticator authenticator,
    string requiredCapability,
    CancellationToken cancellationToken)
{
    var credential = TryParseApiKeyCredential(request.Headers.Authorization.ToString());
    if (credential is null)
    {
        return Results.Json(
            ApiErrorResponse.Create("unauthorized", "API key is missing or invalid."),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var outcome = await authenticator.AuthenticateAsync(
        new ApiKeyAuthenticationRequest(
            credential.Value.KeyId,
            credential.Value.Secret,
            requiredCapability,
            DateTimeOffset.UtcNow,
            request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            request.Headers.UserAgent.ToString()),
        cancellationToken);

    return outcome.Status switch
    {
        ApiKeyAuthenticationStatus.Success => null,
        ApiKeyAuthenticationStatus.Forbidden => Results.Json(
            ApiErrorResponse.Create("forbidden", "API key is not allowed to use the requested capability."),
            statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Json(
            ApiErrorResponse.Create("unauthorized", "API key is missing or invalid."),
            statusCode: StatusCodes.Status401Unauthorized)
    };
}

static ApiKeyCredential? TryParseApiKeyCredential(string authorization)
{
    const string scheme = "ApiKey ";
    if (string.IsNullOrWhiteSpace(authorization)
        || !authorization.StartsWith(scheme, StringComparison.Ordinal))
    {
        return null;
    }

    var value = authorization[scheme.Length..].Trim();
    var separator = value.IndexOf('.', StringComparison.Ordinal);
    if (separator <= 0 || separator == value.Length - 1)
    {
        return null;
    }

    var keyId = value[..separator];
    var secret = value[(separator + 1)..];
    return string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret)
        ? null
        : new ApiKeyCredential(keyId, secret);
}

static async Task<AdminAuthorizationResult> AuthorizeAdminApiAsync(
    HttpRequest request,
    IAdminAuthenticator adminAuthenticator,
    bool requireCsrf,
    CancellationToken cancellationToken)
{
    var outcome = await adminAuthenticator.ValidateSessionAsync(
        new AdminSessionValidationRequest(
            TryReadAdminSessionToken(request),
            request.Headers["X-CSRF-Token"].ToString(),
            requireCsrf,
            DateTimeOffset.UtcNow),
        cancellationToken);

    return outcome.Status switch
    {
        AdminSessionValidationStatus.Success => new AdminAuthorizationResult(null, outcome.Principal),
        AdminSessionValidationStatus.Forbidden => new AdminAuthorizationResult(
            Results.Json(
                ApiErrorResponse.Create("forbidden", "CSRF token is missing or invalid."),
                statusCode: StatusCodes.Status403Forbidden),
            null),
        _ => new AdminAuthorizationResult(
            Results.Json(
                ApiErrorResponse.Create("unauthorized", "Admin session is missing or invalid."),
                statusCode: StatusCodes.Status401Unauthorized),
            null)
    };
}

static string? TryReadAdminSessionToken(HttpRequest request)
{
    return request.Cookies.TryGetValue("centerales_admin_session", out var value)
        ? value
        : null;
}

static void AppendAdminSessionCookie(HttpResponse response, string sessionToken, bool secure)
{
    response.Cookies.Append(
        "centerales_admin_session",
        sessionToken,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api/admin",
            MaxAge = TimeSpan.FromHours(24)
        });
}

static void DeleteAdminSessionCookie(HttpResponse response, bool secure)
{
    response.Cookies.Delete(
        "centerales_admin_session",
        new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api/admin"
        });
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

static AdminJobSupportReportResponse ToAdminJobSupportReportResponse(AdminJobSupportReport report)
{
    return new AdminJobSupportReportResponse(
        report.GeneratedAt,
        report.JobId.ToString("N"),
        report.SubjectId.ToString("N"),
        report.Capability,
        report.ProcessorKey,
        report.ContentHash,
        report.AttemptNumber,
        ToPublicStatus(report.Status),
        report.CreatedAt,
        report.StartedAt,
        report.FinishedAt,
        report.HeartbeatAt,
        new AdminJobSupportReportDiagnosticsResponse(
            report.Diagnostics.Endpoint,
            report.Diagnostics.Duration?.TotalMilliseconds,
            report.Diagnostics.HttpStatus,
            report.Diagnostics.NormalizedError?.ToString(),
            report.Diagnostics.Retryable,
            report.Diagnostics.CorrelationId,
            report.Diagnostics.Excerpt),
        report.Attempts.Select(ToAdminProcessingAttemptResponse).ToArray(),
        report.Result is null
            ? null
            : new AdminJobSupportReportResultReferenceResponse(
                report.Result.ResultIndexId.ToString("N"),
                report.Result.ResultKind,
                report.Result.PayloadTable,
                report.Result.PayloadId.ToString("N"),
                report.Result.ContractVersion,
                report.Result.PayloadSize,
                report.Result.CreatedAt),
        ToAdminProcessorStatusResponse(report.Processor),
        report.AuditEvents.Select(ToAdminJobSupportReportAuditEventResponse).ToArray());
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

static AdminJobSupportReportAuditEventResponse ToAdminJobSupportReportAuditEventResponse(
    AdminJobSupportReportAuditEvent audit)
{
    return new AdminJobSupportReportAuditEventResponse(
        audit.AuditId.ToString("N"),
        audit.OccurredAt,
        audit.ActorLogin,
        audit.Action,
        audit.TargetType,
        audit.TargetId,
        audit.Comment,
        audit.CorrelationId);
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

static AdminUserResponse ToAdminUserResponse(AdminPrincipal principal)
{
    return new AdminUserResponse(
        principal.UserId.ToString("N"),
        principal.Login,
        principal.Role);
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

internal readonly record struct ApiKeyCredential(string KeyId, string Secret);

internal sealed record AdminLoginRequestBody(string? Login, string? Password);

internal sealed record AdminLoginResponse(
    AdminUserResponse Admin,
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt);

internal sealed record AdminMeResponse(AdminUserResponse Admin);

internal sealed record AdminLogoutResponse(bool LoggedOut);

internal sealed record AdminUserResponse(
    string UserId,
    string Login,
    string Role);

internal sealed record AdminAuthorizationResult(IResult? Error, AdminPrincipal? Principal);

internal sealed record AdminManualRetryRequestBody(string? Comment);

internal sealed record AdminManualRetryResponse(
    string SourceJobId,
    string JobId,
    string Hash,
    int AttemptNumber,
    string Status,
    string AuditId);

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

internal sealed record AdminJobSupportReportResponse(
    DateTimeOffset GeneratedAt,
    string JobId,
    string SubjectId,
    string Capability,
    string ProcessorKey,
    string Hash,
    int AttemptNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? HeartbeatAt,
    AdminJobSupportReportDiagnosticsResponse Diagnostics,
    IReadOnlyList<AdminProcessingAttemptResponse> Attempts,
    AdminJobSupportReportResultReferenceResponse? Result,
    AdminProcessorStatusResponse Processor,
    IReadOnlyList<AdminJobSupportReportAuditEventResponse> AuditEvents);

internal sealed record AdminJobSupportReportDiagnosticsResponse(
    string? Endpoint,
    double? DurationMs,
    int? HttpStatus,
    string? NormalizedError,
    bool? Retryable,
    string? CorrelationId,
    string? Excerpt);

internal sealed record AdminJobSupportReportResultReferenceResponse(
    string ResultIndexId,
    string ResultKind,
    string PayloadTable,
    string PayloadId,
    string ContractVersion,
    long PayloadSize,
    DateTimeOffset CreatedAt);

internal sealed record AdminJobSupportReportAuditEventResponse(
    string AuditId,
    DateTimeOffset OccurredAt,
    string? ActorLogin,
    string Action,
    string TargetType,
    string TargetId,
    string? Comment,
    string CorrelationId);

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
