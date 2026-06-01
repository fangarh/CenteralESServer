using CenteralES.AccessControl;

namespace CenteralES.UnitTests;

public sealed class AdminCredentialHasherTests
{
    [Fact]
    public void Password_hasher_accepts_original_password()
    {
        var hash = AdminPasswordHasher.HashPassword("admin-password");

        Assert.True(AdminPasswordHasher.VerifyPassword("admin-password", hash));
    }

    [Fact]
    public void Password_hasher_rejects_wrong_password()
    {
        var hash = AdminPasswordHasher.HashPassword("admin-password");

        Assert.False(AdminPasswordHasher.VerifyPassword("other-password", hash));
    }

    [Fact]
    public void Secure_token_verification_accepts_original_token_only()
    {
        var token = SecureToken.Generate();
        var hash = SecureToken.Hash(token);

        Assert.True(SecureToken.Verify(token, hash));
        Assert.False(SecureToken.Verify(SecureToken.Generate(), hash));
    }
}
