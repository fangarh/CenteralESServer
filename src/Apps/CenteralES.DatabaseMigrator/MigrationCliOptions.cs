namespace CenteralES.DatabaseMigrator;

public sealed record MigrationCliOptions(
    string? ConnectionString,
    bool CreateDatabase,
    bool ShowHelp)
{
    public static MigrationCliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? connectionString = null;
        var createDatabase = true;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-h":
                case "--help":
                    return new MigrationCliParseSuccess(new MigrationCliOptions(null, createDatabase, ShowHelp: true));

                case "--connection-string":
                    if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        return new MigrationCliParseError("Option '--connection-string' requires a value.");
                    }

                    connectionString = args[++index];
                    break;

                case "--no-create-database":
                    createDatabase = false;
                    break;

                default:
                    return new MigrationCliParseError($"Unknown option '{arg}'.");
            }
        }

        return new MigrationCliParseSuccess(new MigrationCliOptions(connectionString, createDatabase, ShowHelp: false));
    }
}

public abstract record MigrationCliParseResult;

public sealed record MigrationCliParseSuccess(MigrationCliOptions Options) : MigrationCliParseResult;

public sealed record MigrationCliParseError(string Message) : MigrationCliParseResult;
