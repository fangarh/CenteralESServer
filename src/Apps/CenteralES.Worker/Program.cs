using CenteralES.Worker;
using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Processing;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing.Queue;
using CenteralES.Processing.Workers;
using CenteralES.Storage;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var processingDatabaseConnectionString = PostgresDatabaseConnectionStringResolver.Resolve(
    builder.Configuration.GetConnectionString("ProcessingDatabase"),
    builder.Environment.ContentRootPath);
var temporaryStorageRoot = TemporaryStorageRootResolver.Resolve(builder.Configuration["Storage:TemporaryRoot"]);
var workerJobProcessorOptions = ResolveWorkerJobProcessorOptions(builder.Configuration);
var workerRecoveryOptions = ResolveWorkerRecoveryOptions(builder.Configuration);
var autoBootstrapDatabase = ShouldAutoBootstrapDatabase(builder.Configuration, builder.Environment);
if (autoBootstrapDatabase)
{
    var databaseBootstrapper = new PostgresDatabaseBootstrapper();
    await databaseBootstrapper.EnsureDatabaseAsync(processingDatabaseConnectionString, CancellationToken.None);
}

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(processingDatabaseConnectionString));
builder.Services.AddSingleton<PostgresProcessingJobQueue>();
builder.Services.AddSingleton<IProcessingJobQueue>(services => services.GetRequiredService<PostgresProcessingJobQueue>());
builder.Services.AddSingleton<IProcessingJobCommandQueue>(services => services.GetRequiredService<PostgresProcessingJobQueue>());
builder.Services.AddSingleton<IProcessingJobClaimQueue>(services => services.GetRequiredService<PostgresProcessingJobQueue>());
builder.Services.AddSingleton<IProcessingJobRecoveryQueue>(services => services.GetRequiredService<PostgresProcessingJobQueue>());
builder.Services.AddSingleton<IWorkerHeartbeatStore, PostgresWorkerHeartbeatStore>();
builder.Services.AddSingleton<IPdfStampRecognitionResultStore, PostgresPdfStampRecognitionResultStore>();
RegisterPdfStampRecognizer(builder.Services, builder.Configuration);
builder.Services.AddSingleton<ITemporaryFileStore>(_ => new LocalTemporaryFileStore(temporaryStorageRoot));
builder.Services.AddSingleton(workerJobProcessorOptions);
builder.Services.AddSingleton(workerRecoveryOptions);
builder.Services.AddSingleton<WorkerJobProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
if (autoBootstrapDatabase)
{
    var databaseBootstrapper = new PostgresDatabaseBootstrapper();
    await databaseBootstrapper.ApplySchemaAsync(host.Services.GetRequiredService<NpgsqlDataSource>(), CancellationToken.None);
}
host.Run();

static void RegisterPdfStampRecognizer(IServiceCollection services, IConfiguration configuration)
{
    var recognizer = configuration["PdfStampRecognition:Recognizer"];
    if (!string.Equals(recognizer, "Http", StringComparison.OrdinalIgnoreCase))
    {
        services.AddSingleton<IPdfStampRecognizer, FakePdfStampRecognizer>();
        return;
    }

    var options = ResolveHttpPdfStampRecognizerOptions(configuration);
    services.AddSingleton(options);
    services.AddSingleton<HttpPdfStampRecognizerEndpointPool>();
    services.AddSingleton(_ => new HttpClient());
    services.AddSingleton<IPdfStampRecognizer, HttpPdfStampRecognizer>();
}

static HttpPdfStampRecognizerOptions ResolveHttpPdfStampRecognizerOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("PdfStampRecognition:Processor");
    var endpoints = section
        .GetSection("endpointPool")
        .GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToArray();

    var options = new HttpPdfStampRecognizerOptions
    {
        EndpointPool = endpoints,
        PoolConcurrencyLimit = ReadPositiveInt(section, "poolConcurrencyLimit", 1),
        EndpointConcurrencyLimit = ReadPositiveInt(section, "endpointConcurrencyLimit", 1),
        Timeout = ReadTimeSpan(section, "timeout", TimeSpan.FromSeconds(30))
    };
    options.Validate();

    return options;
}

static WorkerJobProcessorOptions ResolveWorkerJobProcessorOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("PdfStampRecognition:Processor");
    var options = new WorkerJobProcessorOptions
    {
        MaxAttempts = ReadPositiveInt(section, "maxAttempts", 5),
        ProcessorOverloadedDelay = ReadTimeSpan(section, "processorOverloadedDelay", TimeSpan.FromSeconds(15))
    };
    options.Validate();

    return options;
}

static WorkerRecoveryOptions ResolveWorkerRecoveryOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("PdfStampRecognition:Recovery");
    var options = new WorkerRecoveryOptions
    {
        Enabled = ReadBool(section, "enabled", true),
        StaleJobTimeout = ReadTimeSpan(section, "staleJobTimeout", TimeSpan.FromMinutes(5)),
        RecoveryInterval = ReadTimeSpan(section, "recoveryInterval", TimeSpan.FromMinutes(1)),
        BatchSize = ReadPositiveInt(section, "batchSize", 50)
    };
    options.Validate();

    return options;
}

static int ReadPositiveInt(IConfigurationSection section, string key, int fallback)
{
    var value = section[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return int.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"Configuration value {section.Path}:{key} must be an integer.");
}

static TimeSpan ReadTimeSpan(IConfigurationSection section, string key, TimeSpan fallback)
{
    var value = section[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return TimeSpan.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"Configuration value {section.Path}:{key} must be a TimeSpan.");
}

static bool ReadBool(IConfigurationSection section, string key, bool fallback)
{
    var value = section[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return bool.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"Configuration value {section.Path}:{key} must be a boolean.");
}

static bool ShouldAutoBootstrapDatabase(IConfiguration configuration, IHostEnvironment environment)
{
    var configured = configuration["Database:AutoBootstrap"];
    if (bool.TryParse(configured, out var explicitValue))
    {
        return explicitValue;
    }

    return environment.IsDevelopment();
}
