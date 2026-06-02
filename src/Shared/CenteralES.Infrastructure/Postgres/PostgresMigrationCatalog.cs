using System.Reflection;

namespace CenteralES.Infrastructure.Postgres;

public static class PostgresMigrationCatalog
{
    private const string ResourcePrefix = "CenteralES.Infrastructure.Postgres.Migrations.";
    private const string ResourceSuffix = ".sql";

    public static IReadOnlyList<PostgresMigration> Migrations { get; } = LoadMigrations();

    private static IReadOnlyList<PostgresMigration> LoadMigrations()
    {
        var assembly = typeof(PostgresMigrationCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException("PostgreSQL migrations were not found.");
        }

        var migrations = new List<PostgresMigration>(resourceNames.Length);
        foreach (var resourceName in resourceNames)
        {
            var id = resourceName[ResourcePrefix.Length..^ResourceSuffix.Length];
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"PostgreSQL migration resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream);
            migrations.Add(new PostgresMigration(id, reader.ReadToEnd()));
        }

        var duplicate = migrations
            .GroupBy(migration => migration.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate PostgreSQL migration id '{duplicate.Key}'.");
        }

        return migrations;
    }
}
