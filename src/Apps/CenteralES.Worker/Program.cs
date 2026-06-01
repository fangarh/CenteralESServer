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

var processingDatabaseConnectionString = ResolveProcessingDatabaseConnectionString(builder.Configuration, builder.Environment.ContentRootPath);
var temporaryStorageRoot = ResolveTemporaryStorageRoot(builder.Configuration);
var workerJobProcessorOptions = ResolveWorkerJobProcessorOptions(builder.Configuration);
var databaseBootstrapper = new PostgresDatabaseBootstrapper();
await databaseBootstrapper.EnsureDatabaseAsync(processingDatabaseConnectionString, CancellationToken.None);

builder.Services.AddSingleton(NpgsqlDataSource.Create(processingDatabaseConnectionString));
builder.Services.AddSingleton<IProcessingJobQueue, PostgresProcessingJobQueue>();
builder.Services.AddSingleton<IWorkerHeartbeatStore, PostgresWorkerHeartbeatStore>();
builder.Services.AddSingleton<IPdfStampRecognitionResultStore, PostgresPdfStampRecognitionResultStore>();
RegisterPdfStampRecognizer(builder.Services, builder.Configuration);
builder.Services.AddSingleton<ITemporaryFileStore>(_ => new LocalTemporaryFileStore(temporaryStorageRoot));
builder.Services.AddSingleton(workerJobProcessorOptions);
builder.Services.AddSingleton<WorkerJobProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await databaseBootstrapper.ApplySchemaAsync(host.Services.GetRequiredService<NpgsqlDataSource>(), CancellationToken.None);
host.Run();

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
    services.AddSingleton(new HttpClient());
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

static int ReadPositiveInt(IConfiguration section, string key, int fallback)
{
    var value = section[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return int.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"Configuration value PdfStampRecognition:Processor:{key} must be an integer.");
}

static TimeSpan ReadTimeSpan(IConfiguration section, string key, TimeSpan fallback)
{
    var value = section[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return TimeSpan.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"Configuration value PdfStampRecognition:Processor:{key} must be a TimeSpan.");
}
