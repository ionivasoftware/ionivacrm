using FluentValidation.TestHelper;
using IonCrm.Application.Auth.Commands.Login;

namespace IonCrm.Tests.Auth;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_EmptyEmail_ReturnsValidationError()
    {
        // Arrange
        var command = new LoginCommand(string.Empty, "password123");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Validate_InvalidEmailFormat_ReturnsValidationError()
    {
        // Arrange
        var command = new LoginCommand("not-an-email", "password123");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Validate_EmptyPassword_ReturnsValidationError()
    {
        // Arrange
        var command = new LoginCommand("user@example.com", string.Empty);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Password);
    }

    [Fact]
    public void Validate_ValidEmailAndPassword_PassesValidation()
    {
        // Arrange
        var command = new LoginCommand("user@example.com", "password123");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
