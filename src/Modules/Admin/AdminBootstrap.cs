namespace CenteralES.Admin;

public sealed record AdminBootstrapUserCommand(
    string Login,
    string Password,
    DateTimeOffset RequestedAt,
    string? Comment,
    string Source);

public abstract record AdminBootstrapUserResult;

public sealed record AdminBootstrapUserSuccess(AdminUserListItem User, Guid AuditId) : AdminBootstrapUserResult;

public sealed record AdminBootstrapAlreadyInitialized(int ActiveAdminCount) : AdminBootstrapUserResult;

public sealed record AdminBootstrapLoginConflict(string Login) : AdminBootstrapUserResult;

public sealed record AdminBootstrapInvalidInput(string Message) : AdminBootstrapUserResult;

public interface IAdminBootstrapper
{
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    Task<AdminBootstrapUserResult> BootstrapFirstAdminAsync(
        AdminBootstrapUserCommand command,
        CancellationToken cancellationToken);
}

public static class AdminBootstrapValidator
{
    public static string? Validate(AdminBootstrapUserCommand command, int minimumPasswordLength)
    {
        ArgumentNullException.ThrowIfNull(command);

        var login = command.Login.Trim();
        if (login.Length is < 3 or > 80)
        {
            return "Admin login must be 3-80 characters.";
        }

        if (!login.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            return "Admin login can contain only letters, digits, '.', '_' or '-'.";
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < minimumPasswordLength)
        {
            return $"Password must be at least {minimumPasswordLength} characters.";
        }

        if (command.Comment?.Length > 1000)
        {
            return "Comment must not exceed 1000 characters.";
        }

        if (string.IsNullOrWhiteSpace(command.Source) || command.Source.Length > 100)
        {
            return "Source must be 1-100 characters.";
        }

        return null;
    }
}
