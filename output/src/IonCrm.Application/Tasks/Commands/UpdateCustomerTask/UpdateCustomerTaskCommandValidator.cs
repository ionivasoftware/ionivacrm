using FluentValidation;

namespace IonCrm.Application.Tasks.Commands.UpdateCustomerTask;

/// <summary>Validates <see cref="UpdateCustomerTaskCommand"/>.</summary>
public class UpdateCustomerTaskCommandValidator : AbstractValidator<UpdateCustomerTaskCommand>
{
    public UpdateCustomerTaskCommandValidator()
    {
        RuleFor(t => t.Id)
            .NotEmpty().WithMessage("Task ID is required.");

        RuleFor(t => t.Title)
            .NotEmpty().WithMessage("Task title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(t => t.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
            .When(t => !string.IsNullOrEmpty(t.Description));
    }
}
