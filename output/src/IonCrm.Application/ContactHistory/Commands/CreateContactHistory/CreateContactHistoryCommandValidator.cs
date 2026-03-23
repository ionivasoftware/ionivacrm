using FluentValidation;

namespace IonCrm.Application.ContactHistory.Commands.CreateContactHistory;

/// <summary>Validates <see cref="CreateContactHistoryCommand"/>.</summary>
public class CreateContactHistoryCommandValidator : AbstractValidator<CreateContactHistoryCommand>
{
    public CreateContactHistoryCommandValidator()
    {
        RuleFor(c => c.CustomerId)
            .NotEmpty().WithMessage("Customer ID is required.");

        RuleFor(c => c.ContactedAt)
            .NotEmpty().WithMessage("Contact date is required.")
            .LessThanOrEqualTo(DateTime.UtcNow.AddHours(1))
            .WithMessage("Contact date cannot be more than 1 hour in the future.");

        RuleFor(c => c.Subject)
            .MaximumLength(500).WithMessage("Subject must not exceed 500 characters.")
            .When(c => !string.IsNullOrEmpty(c.Subject));

        RuleFor(c => c.Content)
            .MaximumLength(4000).WithMessage("Content must not exceed 4000 characters.")
            .When(c => !string.IsNullOrEmpty(c.Content));

        RuleFor(c => c.Outcome)
            .MaximumLength(300).WithMessage("Outcome must not exceed 300 characters.")
            .When(c => !string.IsNullOrEmpty(c.Outcome));
    }
}
