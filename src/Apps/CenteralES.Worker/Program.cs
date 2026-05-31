using CenteralES.Worker;
using CenteralES.Infrastructure.Postgres;
using CenteralES.Infrastructure.PdfStampRecognition;
using CenteralES.Infrastructure.Processing;
using CenteralES.PdfStampRecognition;
using CenteralES.Processing.Queue;
using CenteralES.Storage;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

var processingDatabaseConnectionString = ResolveProcessingDatabaseConnectionString(builder.Configuration, builder.Environment.ContentRootPath);
var temporaryStorageRoot = ResolveTemporaryStorageRoot(builder.Configuration);
var databaseBootstrapper = new PostgresDatabaseBootstrapper();
await databaseBootstrapper.EnsureDatabaseAsync(processingDatabaseConnectionString, CancellationToken.None);

builder.Services.AddSingleton(NpgsqlDataSource.Create(processingDatabaseConnectionString));
builder.Services.AddSingleton<IProcessingJobQueue, PostgresProcessingJobQueue>();
builder.Services.AddSingleton<IPdfStampRecognitionResultStore, PostgresPdfStampRecognitionResultStore>();
builder.Services.AddSingleton<IPdfStampRecognizer, FakePdfStampRecognizer>();
builder.Services.AddSingleton<ITemporaryFileStore>(_ => new LocalTemporaryFileStore(temporaryStorageRoot));
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
