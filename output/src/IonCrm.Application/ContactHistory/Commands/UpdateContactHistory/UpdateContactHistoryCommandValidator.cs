using FluentValidation;

namespace IonCrm.Application.ContactHistory.Commands.UpdateContactHistory;

/// <summary>Validates <see cref="UpdateContactHistoryCommand"/>.</summary>
public class UpdateContactHistoryCommandValidator : AbstractValidator<UpdateContactHistoryCommand>
{
    public UpdateContactHistoryCommandValidator()
    {
        RuleFor(c => c.Id)
            .NotEmpty().WithMessage("Contact history ID is required.");

        RuleFor(c => c.ContactedAt)
            .NotEmpty().WithMessage("Contact date is required.");

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
