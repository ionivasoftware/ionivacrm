using FluentValidation;

namespace IonCrm.Application.Tasks.Commands.UpdateTaskStatus;

/// <summary>Validates <see cref="UpdateTaskStatusCommand"/>.</summary>
public class UpdateTaskStatusCommandValidator : AbstractValidator<UpdateTaskStatusCommand>
{
    public UpdateTaskStatusCommandValidator()
    {
        RuleFor(c => c.Id)
            .NotEmpty().WithMessage("Task ID is required.");

        RuleFor(c => c.Status)
            .IsInEnum().WithMessage("Status must be a valid TaskStatus value.");
    }
}
