namespace CenteralES.AccessControl;

public static class AdminPasswordHasher
{
    public const int MinimumPasswordLength = 8;

    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return Pbkdf2SecretHasher.HashSecret(password);
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        return Pbkdf2SecretHasher.VerifySecret(password, storedHash);
    }
}
