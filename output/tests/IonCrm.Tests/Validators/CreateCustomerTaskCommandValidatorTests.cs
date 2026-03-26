using FluentValidation.TestHelper;
using IonCrm.Application.Tasks.Commands.CreateCustomerTask;

namespace IonCrm.Tests.Validators;

/// <summary>
/// Tests for <see cref="CreateCustomerTaskCommandValidator"/>.
/// Validates title, description, due date, and customer ID rules.
/// </summary>
public class CreateCustomerTaskCommandValidatorTests
{
    private readonly CreateCustomerTaskCommandValidator _validator = new();

    private static CreateCustomerTaskCommand ValidCommand() => new()
    {
        CustomerId = Guid.NewGuid(),
        Title = "Follow up with customer",
        Description = "Call back regarding proposal.",
        DueDate = DateTime.UtcNow.AddDays(2)
    };

    [Fact]
    public void Should_NotHaveError_When_CommandIsValid()
    {
        var command = ValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_HaveError_When_CustomerIdIsEmpty()
    {
        var command = ValidCommand() with { CustomerId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.CustomerId);
    }

    [Fact]
    public void Should_HaveError_When_TitleIsEmpty()
    {
        var command = ValidCommand() with { Title = string.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Title);
    }

    [Fact]
    public void Should_HaveError_When_TitleIsNull()
    {
        var command = ValidCommand() with { Title = null! };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Title);
    }

    [Fact]
    public void Should_HaveError_When_TitleExceeds500Characters()
    {
        var command = ValidCommand() with { Title = new string('A', 501) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Title);
    }

    [Fact]
    public void Should_NotHaveError_When_TitleIsExactly500Characters()
    {
        var command = ValidCommand() with { Title = new string('A', 500) };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.Title);
    }

    [Fact]
    public void Should_HaveError_When_DescriptionExceeds2000Characters()
    {
        var command = ValidCommand() with { Description = new string('D', 2001) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void Should_NotHaveError_When_DescriptionIsExactly2000Characters()
    {
        var command = ValidCommand() with { Description = new string('D', 2000) };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void Should_NotHaveError_When_DescriptionIsNull()
    {
        // Description is optional
        var command = ValidCommand() with { Description = null };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void Should_HaveError_When_DueDateIsInThePast()
    {
        var command = ValidCommand() with { DueDate = DateTime.UtcNow.AddDays(-1) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.DueDate);
    }

    [Fact]
    public void Should_NotHaveError_When_DueDateIsInTheFuture()
    {
        var command = ValidCommand() with { DueDate = DateTime.UtcNow.AddHours(1) };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.DueDate);
    }

    [Fact]
    public void Should_NotHaveError_When_DueDateIsNull()
    {
        // Due date is optional
        var command = ValidCommand() with { DueDate = null };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.DueDate);
    }

    [Fact]
    public void Should_NotHaveError_When_OnlyRequiredFieldsProvided()
    {
        var command = new CreateCustomerTaskCommand
        {
            CustomerId = Guid.NewGuid(),
            Title = "Minimal task"
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
