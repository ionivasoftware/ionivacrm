using FluentValidation.TestHelper;
using IonCrm.Application.Auth.Commands.RegisterUser;

namespace IonCrm.Tests.Validators;

/// <summary>
/// Tests for RegisterUserCommandValidator — stronger password policy than Login.
/// Requires uppercase, lowercase, digit; email and name fields also validated.
/// </summary>
public class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator _validator = new();

    // ── Email rules ───────────────────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_EmailIsEmpty()
    {
        var cmd = new RegisterUserCommand("", "SecurePass1!", "Jane", "Doe");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_HaveError_When_EmailIsInvalidFormat()
    {
        var cmd = new RegisterUserCommand("not-an-email", "SecurePass1!", "Jane", "Doe");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Email);
    }

    // ── Password strength rules ───────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_PasswordIsEmpty()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "", "Jane", "Doe");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_HaveError_When_PasswordTooShort()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "Ab1!", "Jane", "Doe");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_HaveError_When_PasswordHasNoUppercase()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "lowercase1!", "Jane", "Doe");
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one uppercase letter.");
    }

    [Fact]
    public void Should_HaveError_When_PasswordHasNoLowercase()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "UPPERCASE1!", "Jane", "Doe");
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one lowercase letter.");
    }

    [Fact]
    public void Should_HaveError_When_PasswordHasNoDigit()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "NoDigitPass!", "Jane", "Doe");
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one digit.");
    }

    // ── First and last name rules ─────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_FirstNameIsEmpty()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "SecurePass1!", "", "Doe");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public void Should_HaveError_When_LastNameIsEmpty()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "SecurePass1!", "Jane", "");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.LastName);
    }

    [Fact]
    public void Should_HaveError_When_FirstNameExceeds100Characters()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "SecurePass1!", new string('a', 101), "Doe");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public void Should_HaveError_When_LastNameExceeds100Characters()
    {
        var cmd = new RegisterUserCommand("jane@example.com", "SecurePass1!", "Jane", new string('a', 101));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.LastName);
    }

    // ── Valid command ─────────────────────────────────────────────────────────

    [Fact]
    public void Should_NotHaveAnyErrors_When_AllFieldsMeetRequirements()
    {
        var cmd = new RegisterUserCommand("admin@ioncrm.com", "SecurePass1!", "Jane", "Doe");
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }
}
