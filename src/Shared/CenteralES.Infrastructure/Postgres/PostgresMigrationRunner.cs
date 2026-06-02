using Npgsql;

namespace CenteralES.Infrastructure.Postgres;

public sealed class PostgresMigrationRunner
{
    private const string EnsureMigrationTableSql = """
        create table if not exists schema_migrations (
            id text primary key,
            applied_at timestamptz not null
        );
        """;

    public async Task<IReadOnlyList<string>> ApplyAsync(
        NpgsqlDataSource dataSource,
        IReadOnlyList<PostgresMigration> migrations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(migrations);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await EnsureMigrationTableAsync(connection, cancellationToken);

        var appliedIds = await ReadAppliedMigrationIdsAsync(connection, cancellationToken);
        var newlyApplied = new List<string>();

        foreach (var migration in migrations.OrderBy(migration => migration.Id, StringComparer.Ordinal))
        {
            if (appliedIds.Contains(migration.Id))
            {
                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var migrationCommand = new NpgsqlCommand(migration.Sql, connection, transaction);
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var markerCommand = new NpgsqlCommand("""
                insert into schema_migrations (id, applied_at)
                values (@id, @applied_at);
                """, connection, transaction);
            markerCommand.Parameters.AddWithValue("id", migration.Id);
            markerCommand.Parameters.AddWithValue("applied_at", DateTimeOffset.UtcNow);
            await markerCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            appliedIds.Add(migration.Id);
            newlyApplied.Add(migration.Id);
        }

        return newlyApplied;
    }

    private static async Task EnsureMigrationTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(EnsureMigrationTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> ReadAppliedMigrationIdsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select id
            from schema_migrations
            order by id;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var appliedIds = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            appliedIds.Add(reader.GetString(0));
        }

        return appliedIds;
    }
}
