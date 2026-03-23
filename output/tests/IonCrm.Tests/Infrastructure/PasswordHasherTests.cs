using IonCrm.Infrastructure.Services;

namespace IonCrm.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="PasswordHasher"/>.
/// </summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    // ── Hash ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Hash_ValidPassword_ReturnsBcryptHash()
    {
        // Act
        var hash = _hasher.Hash("S3cur3P@ss!");

        // Assert
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().StartWith("$2a$12$");  // BCrypt cost-12 prefix
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        // Act — BCrypt uses random salt each call
        var hash1 = _hasher.Hash("same_password");
        var hash2 = _hasher.Hash("same_password");

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_EmptyOrWhitespace_ThrowsArgumentException(string password)
    {
        // Act & Assert
        var act = () => _hasher.Hash(password);
        act.Should().Throw<ArgumentException>();
    }

    // ── Verify ────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "correct_password";
        var hash     = _hasher.Hash(password);

        // Act
        var result = _hasher.Verify(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        // Arrange
        var hash = _hasher.Hash("original_password");

        // Act
        var result = _hasher.Verify("wrong_password", hash);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "$2a$12$validhash")]
    [InlineData("password", "")]
    [InlineData("", "")]
    public void Verify_EmptyInputs_ReturnsFalse(string password, string hash)
    {
        // Act
        var result = _hasher.Verify(password, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        // Act
        var result = _hasher.Verify("password", "not-a-valid-bcrypt-hash");

        // Assert
        result.Should().BeFalse();
    }
}
