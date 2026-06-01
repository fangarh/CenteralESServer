using CenteralES.AccessControl;

namespace CenteralES.UnitTests;

public sealed class ApiKeySecretHasherTests
{
    [Fact]
    public void VerifySecret_accepts_original_secret()
    {
        var hash = ApiKeySecretHasher.HashSecret("secret-value");

        Assert.True(ApiKeySecretHasher.VerifySecret("secret-value", hash));
    }

    [Fact]
    public void VerifySecret_rejects_wrong_secret()
    {
        var hash = ApiKeySecretHasher.HashSecret("secret-value");

        Assert.False(ApiKeySecretHasher.VerifySecret("other-secret", hash));
    }

    [Fact]
    public void VerifySecret_rejects_malformed_hash()
    {
        Assert.False(ApiKeySecretHasher.VerifySecret("secret-value", "not-a-valid-hash"));
    }
}
