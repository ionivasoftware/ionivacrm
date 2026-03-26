using FluentValidation.TestHelper;
using IonCrm.Application.Tasks.Commands.UpdateCustomerTask;
using IonCrm.Domain.Enums;
using TaskStatus = IonCrm.Domain.Enums.TaskStatus;

namespace IonCrm.Tests.Validators;

/// <summary>
/// Tests for <see cref="UpdateCustomerTaskCommandValidator"/>.
/// Validates ID, title, and description rules on task update.
/// </summary>
public class UpdateCustomerTaskCommandValidatorTests
{
    private readonly UpdateCustomerTaskCommandValidator _validator = new();

    private static UpdateCustomerTaskCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Updated task title",
        Description = "Updated description.",
        Priority = TaskPriority.High,
        Status = TaskStatus.InProgress
    };

    [Fact]
    public void Should_NotHaveError_When_CommandIsValid()
    {
        var command = ValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_HaveError_When_IdIsEmpty()
    {
        var command = ValidCommand() with { Id = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Id);
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
        var command = ValidCommand() with { Title = new string('X', 501) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Title);
    }

    [Fact]
    public void Should_NotHaveError_When_TitleIsExactly500Characters()
    {
        var command = ValidCommand() with { Title = new string('X', 500) };
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
        // Description is optional — null is fine
        var command = ValidCommand() with { Description = null };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void Should_NotHaveError_When_DescriptionIsEmpty()
    {
        // Empty string triggers the "when not null/empty" conditional — should NOT error
        var command = ValidCommand() with { Description = string.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void Should_NotHaveError_When_AllFieldsAreValid()
    {
        var command = new UpdateCustomerTaskCommand
        {
            Id = Guid.NewGuid(),
            Title = "Full update",
            Description = "Complete description here.",
            Priority = TaskPriority.Critical,
            Status = TaskStatus.Done,
            AssignedUserId = Guid.NewGuid(),
            DueDate = DateTime.UtcNow.AddDays(7)
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
