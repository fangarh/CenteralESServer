using CenteralES.DatabaseMigrator;
using CenteralES.Infrastructure.Postgres;
using Npgsql;

var parseResult = MigrationCliOptions.Parse(args);
switch (parseResult)
{
    case MigrationCliParseError error:
        Console.Error.WriteLine(error.Message);
        PrintUsage(Console.Error);
        return 2;

    case MigrationCliParseSuccess { Options.ShowHelp: true }:
        PrintUsage(Console.Out);
        return 0;

    case MigrationCliParseSuccess success:
        return await RunAsync(success.Options, CancellationToken.None);

    default:
        Console.Error.WriteLine("Unsupported command line parse result.");
        return 2;
}

static async Task<int> RunAsync(MigrationCliOptions options, CancellationToken cancellationToken)
{
    try
    {
        var connectionString = PostgresDatabaseConnectionStringResolver.Resolve(
            options.ConnectionString,
            Directory.GetCurrentDirectory());

        var bootstrapper = new PostgresDatabaseBootstrapper();
        if (options.CreateDatabase)
        {
            Console.WriteLine("Ensuring target PostgreSQL database exists...");
            await bootstrapper.EnsureDatabaseAsync(connectionString, cancellationToken);
        }
        else
        {
            Console.WriteLine("Skipping target PostgreSQL database creation.");
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        Console.WriteLine("Applying PostgreSQL schema migrations...");
        var applied = await new PostgresMigrationRunner().ApplyAsync(
            dataSource,
            PostgresMigrationCatalog.Migrations,
            cancellationToken);

        if (applied.Count == 0)
        {
            Console.WriteLine("No pending migrations.");
            return 0;
        }

        Console.WriteLine($"Applied migrations: {string.Join(", ", applied)}.");
        return 0;
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        Console.Error.WriteLine($"Migration failed: {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

static void PrintUsage(TextWriter writer)
{
    writer.WriteLine("Usage:");
    writer.WriteLine("  CenteralES.DatabaseMigrator [--connection-string <value>] [--no-create-database]");
    writer.WriteLine();
    writer.WriteLine("Options:");
    writer.WriteLine("  --connection-string <value>  PostgreSQL connection string. If omitted, CENTERALES_PROCESSING_DATABASE or db.env is used.");
    writer.WriteLine("  --no-create-database         Skip CREATE DATABASE and only apply schema migrations to the target database.");
    writer.WriteLine("  -h, --help                   Show this help.");
}
