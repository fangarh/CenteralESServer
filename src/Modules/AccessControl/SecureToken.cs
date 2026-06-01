using System.Security.Cryptography;
using System.Text;

namespace CenteralES.AccessControl;

public static class SecureToken
{
    private const string Algorithm = "sha256";
    private const int TokenBytes = 32;

    public static string Generate()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
    }

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return $"{Algorithm}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string token, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var actualHash = Hash(token);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualHash),
            Encoding.UTF8.GetBytes(storedHash));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }
}
