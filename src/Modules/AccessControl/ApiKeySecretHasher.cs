namespace CenteralES.AccessControl;

public static class ApiKeySecretHasher
{
    public static string HashSecret(string secret)
    {
        return Pbkdf2SecretHasher.HashSecret(secret);
    }

    public static bool VerifySecret(string secret, string storedHash)
    {
        return Pbkdf2SecretHasher.VerifySecret(secret, storedHash);
    }
}
