using CenteralES.Infrastructure.Postgres;
using Npgsql;

namespace CenteralES.IntegrationTests;

internal static class IntegrationTestDatabase
{
    public static string? TryReadConnectionString()
    {
        var envPath = Path.Combine(GetRepositoryRoot(), "db.env");
        if (!File.Exists(envPath))
        {
            return null;
        }

        try
        {
            var connectionString = PostgresConnectionString.ReadFromEnvFile(envPath, "test_db");
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                SslMode = SslMode.Disable
            };

            return builder.ConnectionString;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CenteralESServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
