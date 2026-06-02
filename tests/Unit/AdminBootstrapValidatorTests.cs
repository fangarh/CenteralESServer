using CenteralES.Admin;

namespace CenteralES.UnitTests;

public sealed class AdminBootstrapValidatorTests
{
    [Fact]
    public void Validate_accepts_valid_first_admin_command()
    {
        var error = AdminBootstrapValidator.Validate(
            new AdminBootstrapUserCommand(
                "admin.bootstrap",
                "password-123",
                DateTimeOffset.UtcNow,
                "first admin",
                "unit_test"),
            minimumPasswordLength: 8);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("ab", "password-123", "unit_test")]
    [InlineData("admin space", "password-123", "unit_test")]
    [InlineData("admin", "short", "unit_test")]
    [InlineData("admin", "password-123", "")]
    public void Validate_rejects_invalid_first_admin_command(string login, string password, string source)
    {
        var error = AdminBootstrapValidator.Validate(
            new AdminBootstrapUserCommand(
                login,
                password,
                DateTimeOffset.UtcNow,
                null,
                source),
            minimumPasswordLength: 8);

        Assert.NotNull(error);
    }
}
