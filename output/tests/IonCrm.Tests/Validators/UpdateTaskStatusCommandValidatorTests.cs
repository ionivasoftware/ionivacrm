using FluentValidation.TestHelper;
using IonCrm.Application.Tasks.Commands.UpdateTaskStatus;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.Validators;

public class UpdateTaskStatusCommandValidatorTests
{
    private readonly UpdateTaskStatusCommandValidator _validator = new();

    [Fact]
    public void Should_NotHaveError_When_CommandIsValid()
    {
        var command = new UpdateTaskStatusCommand(Guid.NewGuid(), IonCrm.Domain.Enums.TaskStatus.Done);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_HaveError_When_IdIsEmpty()
    {
        var command = new UpdateTaskStatusCommand(Guid.Empty, IonCrm.Domain.Enums.TaskStatus.InProgress);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Id);
    }

    [Fact]
    public void Should_HaveError_When_StatusIsInvalid()
    {
        var command = new UpdateTaskStatusCommand(Guid.NewGuid(), (IonCrm.Domain.Enums.TaskStatus)999);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Status);
    }
}
