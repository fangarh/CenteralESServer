namespace CenteralES.Infrastructure.Postgres;

public static class PostgresDatabaseConnectionStringResolver
{
    public static string Resolve(string? configuredConnectionString, string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return configuredConnectionString;
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
}
