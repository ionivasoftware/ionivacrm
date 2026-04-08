using FluentValidation;

namespace IonCrm.Application.Customers.Commands.CreateCustomerContract;

/// <summary>Validates <see cref="CreateCustomerContractCommand"/>.</summary>
public class CreateCustomerContractCommandValidator : AbstractValidator<CreateCustomerContractCommand>
{
    public CreateCustomerContractCommandValidator()
    {
        RuleFor(c => c.CustomerId)
            .NotEmpty().WithMessage("Müşteri ID zorunludur.");

        RuleFor(c => c.MonthlyAmount)
            .GreaterThan(0).WithMessage("Aylık tutar 0'dan büyük olmalıdır.");

        RuleFor(c => c.StartDate)
            .NotEmpty().WithMessage("Başlangıç tarihi zorunludur.");

        RuleFor(c => c.DurationMonths)
            .InclusiveBetween(1, 120)
                .WithMessage("Süre 1 ile 120 ay arasında olmalıdır.")
            .When(c => c.DurationMonths.HasValue);
    }
}
