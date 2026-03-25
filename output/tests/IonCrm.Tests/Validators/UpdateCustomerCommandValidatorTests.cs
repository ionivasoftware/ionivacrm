using FluentValidation.TestHelper;
using IonCrm.Application.Customers.Commands.UpdateCustomer;

namespace IonCrm.Tests.Validators;

/// <summary>
/// Unit tests for <see cref="UpdateCustomerCommandValidator"/>.
/// Covers required fields, field-length limits, and email format validation.
/// </summary>
public class UpdateCustomerCommandValidatorTests
{
    private readonly UpdateCustomerCommandValidator _validator = new();

    // ── Required fields ───────────────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_IdIsEmpty()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.Empty,
            CompanyName = "Valid Corp"
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Id);
    }

    [Fact]
    public void Should_HaveError_When_CompanyNameIsEmpty()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = ""
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.CompanyName);
    }

    [Fact]
    public void Should_HaveError_When_CompanyNameIsWhitespace()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "   "
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.CompanyName);
    }

    // ── Length limits ─────────────────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_CompanyNameExceeds300Characters()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = new string('X', 301)
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.CompanyName);
    }

    [Fact]
    public void Should_HaveError_When_CodeExceeds50Characters()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "Valid Corp",
            Code = new string('A', 51)
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Code);
    }

    [Fact]
    public void Should_HaveError_When_PhoneExceeds50Characters()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "Valid Corp",
            Phone = new string('5', 51)
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Phone);
    }

    [Fact]
    public void Should_HaveError_When_EmailExceeds256Characters()
    {
        // Arrange — 252 chars before @x.com = 258 chars total → over the 256 limit
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "Valid Corp",
            Email = new string('a', 252) + "@x.com"
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    // ── Email format ──────────────────────────────────────────────────────────

    [Fact]
    public void Should_HaveError_When_EmailIsInvalidFormat()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "Corp",
            Email = "not-an-email"
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Should_HaveError_When_EmailMissingDomain()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "Corp",
            Email = "user@"
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    // ── Happy-path scenarios ──────────────────────────────────────────────────

    [Fact]
    public void Should_NotHaveError_When_CommandIsValid()
    {
        // Arrange
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "Updated Corp",
            Email = "contact@updated.com",
            Phone = "555-9876",
            Code = "UPDT-01"
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_NotHaveError_When_OptionalFieldsAreNull()
    {
        // Arrange — only required fields provided
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "Minimal Corp"
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_NotHaveError_When_EmailIsNull()
    {
        // Arrange — email is optional; null should not trigger email-format validation
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = "No Email Corp",
            Email = null
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Should_NotHaveError_When_CompanyNameIsExactly300Characters()
    {
        // Arrange — boundary: exactly at max length
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            CompanyName = new string('X', 300)
        };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.CompanyName);
    }
}
