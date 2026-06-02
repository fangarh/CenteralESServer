using CenteralES.DatabaseMigrator;

namespace CenteralES.UnitTests;

public sealed class MigrationCliOptionsTests
{
    [Fact]
    public void Parse_defaults_to_create_database()
    {
        var result = Assert.IsType<MigrationCliParseSuccess>(MigrationCliOptions.Parse([]));

        Assert.Null(result.Options.ConnectionString);
        Assert.True(result.Options.CreateDatabase);
        Assert.False(result.Options.ShowHelp);
    }

    [Fact]
    public void Parse_accepts_connection_string_and_no_create_database()
    {
        var result = Assert.IsType<MigrationCliParseSuccess>(MigrationCliOptions.Parse([
            "--connection-string",
            "Host=localhost;Database=test_db",
            "--no-create-database"
        ]));

        Assert.Equal("Host=localhost;Database=test_db", result.Options.ConnectionString);
        Assert.False(result.Options.CreateDatabase);
    }

    [Fact]
    public void Parse_returns_help_without_requiring_other_options()
    {
        var result = Assert.IsType<MigrationCliParseSuccess>(MigrationCliOptions.Parse(["--help"]));

        Assert.True(result.Options.ShowHelp);
    }

    [Fact]
    public void Parse_rejects_unknown_option()
    {
        var result = Assert.IsType<MigrationCliParseError>(MigrationCliOptions.Parse(["--unknown"]));

        Assert.Contains("--unknown", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_connection_string_without_value()
    {
        var result = Assert.IsType<MigrationCliParseError>(MigrationCliOptions.Parse(["--connection-string"]));

        Assert.Contains("--connection-string", result.Message, StringComparison.Ordinal);
    }
}
