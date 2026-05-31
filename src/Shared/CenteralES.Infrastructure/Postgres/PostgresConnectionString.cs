using Npgsql;

namespace CenteralES.Infrastructure.Postgres;

public static class PostgresConnectionString
{
    public static string ReadFromEnvFile(string path, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var prefix = key + "=";
        var line = File.ReadLines(path)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (line is null)
        {
            throw new InvalidOperationException($"Connection string key '{key}' was not found in '{path}'.");
        }

        return line.Split('=', 2)[1].Trim().Trim('"');
    }

    public static string WithDatabase(string connectionString, string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database
        };

        return builder.ConnectionString;
    }
}
