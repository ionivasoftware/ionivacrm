using FluentValidation;

namespace IonCrm.Application.Auth.Commands.Login;

/// <summary>
/// FluentValidation rules for <see cref="LoginCommand"/>.
/// Runs automatically via the MediatR ValidationBehaviour pipeline.
/// </summary>
public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    /// <summary>Initialises a new instance of <see cref="LoginCommandValidator"/>.</summary>
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.");
    }
}
