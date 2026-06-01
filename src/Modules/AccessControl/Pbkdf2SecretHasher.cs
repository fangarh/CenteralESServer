using System.Security.Cryptography;

namespace CenteralES.AccessControl;

internal static class Pbkdf2SecretHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int SaltBytes = 32;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000;

    public static string HashSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return string.Join(
            ':',
            Algorithm,
            DefaultIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool VerifySecret(string secret, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
