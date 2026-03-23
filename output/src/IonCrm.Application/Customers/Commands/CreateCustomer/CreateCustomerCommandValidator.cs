using FluentValidation;

namespace IonCrm.Application.Customers.Commands.CreateCustomer;

/// <summary>Validates <see cref="CreateCustomerCommand"/>.</summary>
public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(c => c.ProjectId)
            .NotEmpty().WithMessage("Project ID is required.");

        RuleFor(c => c.CompanyName)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(300).WithMessage("Company name must not exceed 300 characters.");

        RuleFor(c => c.Code)
            .MaximumLength(50).WithMessage("Code must not exceed 50 characters.")
            .When(c => !string.IsNullOrEmpty(c.Code));

        RuleFor(c => c.ContactName)
            .MaximumLength(200).WithMessage("Contact name must not exceed 200 characters.")
            .When(c => !string.IsNullOrEmpty(c.ContactName));

        RuleFor(c => c.Email)
            .EmailAddress().WithMessage("Invalid email format.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.")
            .When(c => !string.IsNullOrEmpty(c.Email));

        RuleFor(c => c.Phone)
            .MaximumLength(50).WithMessage("Phone must not exceed 50 characters.")
            .When(c => !string.IsNullOrEmpty(c.Phone));
    }
}
