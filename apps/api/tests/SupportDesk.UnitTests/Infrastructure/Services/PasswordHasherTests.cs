using SupportDesk.Infrastructure.Services;
using Xunit;

namespace SupportDesk.UnitTests.Infrastructure.Services;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        var password = "SecurePassword123!";
        var hash = _hasher.HashPassword(password);

        var result = _hasher.VerifyPassword(password, hash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ReturnsFalse()
    {
        var password = "SecurePassword123!";
        var hash = _hasher.HashPassword(password);

        var result = _hasher.VerifyPassword("WrongPassword123!", hash);

        Assert.False(result);
    }

    [Fact]
    public void HashPassword_GeneratesDifferentSalt_ForSamePassword()
    {
        var password = "SecurePassword123!";

        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
        Assert.True(_hasher.VerifyPassword(password, hash1));
        Assert.True(_hasher.VerifyPassword(password, hash2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-hash")]
    [InlineData("too:few:parts")]
    public void VerifyPassword_WithInvalidHashFormat_ReturnsFalse(string invalidHash)
    {
        var result = _hasher.VerifyPassword("password", invalidHash);

        Assert.False(result);
    }
}
