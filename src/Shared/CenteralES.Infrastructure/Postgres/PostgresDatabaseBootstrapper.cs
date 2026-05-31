using System.Text.RegularExpressions;
using Npgsql;

namespace CenteralES.Infrastructure.Postgres;

public sealed class PostgresDatabaseBootstrapper
{
    private static readonly Regex SafeDatabaseName = new("^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

    public async Task EnsureDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDatabase = builder.Database;

        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            throw new InvalidOperationException("Connection string must contain a target database.");
        }

        if (!SafeDatabaseName.IsMatch(targetDatabase))
        {
            throw new InvalidOperationException("Database name contains unsupported characters.");
        }

        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var existsCommand = new NpgsqlCommand("select 1 from pg_database where datname = @database", connection);
        existsCommand.Parameters.AddWithValue("database", targetDatabase);

        var exists = await existsCommand.ExecuteScalarAsync(cancellationToken);
        if (exists is not null)
        {
            return;
        }

        await using var createCommand = new NpgsqlCommand($"""create database "{targetDatabase}";""", connection);
        await createCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplySchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Processing.PostgresProcessingSql.Schema, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
