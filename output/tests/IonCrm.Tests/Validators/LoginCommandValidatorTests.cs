using FluentValidation.TestHelper;
using IonCrm.Application.Auth.Commands.Login;

namespace IonCrm.Tests.Validators;

/// <summary>
/// Tests for LoginCommandValidator.
/// Validates email format, presence checks, and password length requirements.
/// These run via the MediatR ValidationBehaviour pipeline before the handler executes.
/// </summary>
public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    // ── Email validation ─────────────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_EmailIsEmpty()
    {
        // Arrange
        var command = new LoginCommand("", "ValidPass1!");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email)
            .WithErrorMessage("Email is required.");
    }

    [Fact]
    public void Should_HaveError_When_EmailIsInvalidFormat()
    {
        // Arrange
        var command = new LoginCommand("not-an-email", "ValidPass1!");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email)
            .WithErrorMessage("A valid email address is required.");
    }

    [Fact]
    public void Should_HaveError_When_EmailExceeds256Characters()
    {
        // Arrange — 257-char email: 251 'a' chars + "@x.com" (6 chars) = 257 total
        // MaximumLength(256) allows up to and including 256; 257 should fail.
        var longEmail = new string('a', 251) + "@x.com";  // 257 chars total
        var command = new LoginCommand(longEmail, "ValidPass1!");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Should_NotHaveError_When_EmailIsValid()
    {
        // Arrange
        var command = new LoginCommand("user@example.com", "ValidPass1!");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.Email);
    }

    // ── Password validation ──────────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_PasswordIsEmpty()
    {
        // Arrange
        var command = new LoginCommand("user@example.com", "");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Password)
            .WithErrorMessage("Password is required.");
    }

    [Fact]
    public void Should_HaveError_When_PasswordIsTooShort()
    {
        // Arrange — 7 chars (minimum is 8)
        var command = new LoginCommand("user@example.com", "Short1!");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Password)
            .WithErrorMessage("Password must be at least 8 characters.");
    }

    [Fact]
    public void Should_HaveError_When_PasswordExceeds128Characters()
    {
        // Arrange — 129-char password
        var longPassword = new string('a', 129);
        var command = new LoginCommand("user@example.com", longPassword);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Password)
            .WithErrorMessage("Password must not exceed 128 characters.");
    }

    [Fact]
    public void Should_NotHaveError_When_PasswordMeetsMinLength()
    {
        // Arrange — exactly 8 characters
        var command = new LoginCommand("user@example.com", "Secure1!");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.Password);
    }

    // ── Full valid command ───────────────────────────────────────────────────

    [Fact]
    public void Should_NotHaveAnyErrors_When_CommandIsValid()
    {
        // Arrange
        var command = new LoginCommand("admin@ioncrm.com", "SecurePass123!");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Multiple errors returned simultaneously ──────────────────────────────

    [Fact]
    public void Should_HaveMultipleErrors_When_BothEmailAndPasswordAreInvalid()
    {
        // Arrange
        var command = new LoginCommand("", "");

        // Act
        var result = _validator.TestValidate(command);

        // Assert — both fields fail
        result.ShouldHaveValidationErrorFor(c => c.Email);
        result.ShouldHaveValidationErrorFor(c => c.Password);
    }
}
