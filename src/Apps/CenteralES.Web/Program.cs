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

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddOpenApi();

var processingDatabaseConnectionString = PostgresDatabaseConnectionStringResolver.Resolve(
    builder.Configuration.GetConnectionString("ProcessingDatabase"),
    builder.Environment.ContentRootPath);
var temporaryStorageRoot = TemporaryStorageRootResolver.Resolve(builder.Configuration["Storage:TemporaryRoot"]);
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
builder.Services.AddSingleton(new AdminStorageOptions(temporaryStorageRoot));
builder.Services.AddSingleton(new AdminSettingsOptions(pdfMaxUploadBytes, temporaryStorageRoot, temporaryStorageLimits));
builder.Services.AddSingleton<IAdminProcessingReadStore, PostgresAdminProcessingReadStore>();
builder.Services.AddSingleton<IAdminProcessingActionStore, PostgresAdminProcessingActionStore>();
builder.Services.AddSingleton<IAdminApiKeyStore, PostgresAdminApiKeyStore>();
builder.Services.AddSingleton<IAdminUserStore, PostgresAdminUserStore>();

var app = builder.Build();

await databaseBootstrapper.ApplySchemaAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), CancellationToken.None);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.MapAdminUiEndpoints();
app.MapHealthEndpoints();
app.MapPublicPdfEndpoints(pdfMaxUploadBytes);
app.MapAdminAuthEndpoints();
app.MapAdminJobEndpoints();
app.MapAdminProcessorEndpoints();
app.MapAdminAuditEndpoints();
app.MapAdminStorageEndpoints();
app.MapAdminResultEndpoints();
app.MapAdminSettingsEndpoints();
app.MapAdminApiKeyEndpoints();
app.MapAdminUserEndpoints();

app.Run();

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

public partial class Program;
