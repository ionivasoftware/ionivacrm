using FluentValidation;

namespace IonCrm.Application.Tasks.Commands.CreateCustomerTask;

/// <summary>Validates <see cref="CreateCustomerTaskCommand"/>.</summary>
public class CreateCustomerTaskCommandValidator : AbstractValidator<CreateCustomerTaskCommand>
{
    public CreateCustomerTaskCommandValidator()
    {
        RuleFor(t => t.CustomerId)
            .NotEmpty().WithMessage("Customer ID is required.");

        RuleFor(t => t.Title)
            .NotEmpty().WithMessage("Task title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(t => t.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
            .When(t => !string.IsNullOrEmpty(t.Description));

        RuleFor(t => t.DueDate)
            .GreaterThan(DateTime.UtcNow.AddMinutes(-5))
            .WithMessage("Due date must be in the future.")
            .When(t => t.DueDate.HasValue);
    }
}
