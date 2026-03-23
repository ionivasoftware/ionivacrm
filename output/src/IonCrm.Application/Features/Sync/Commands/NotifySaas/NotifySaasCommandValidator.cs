using FluentValidation;

namespace IonCrm.Application.Features.Sync.Commands.NotifySaas;

/// <summary>FluentValidation rules for <see cref="NotifySaasCommand"/>.</summary>
public sealed class NotifySaasCommandValidator : AbstractValidator<NotifySaasCommand>
{
    /// <summary>Initialises validation rules.</summary>
    public NotifySaasCommandValidator()
    {
        RuleFor(x => x.EventType)
            .NotEmpty().WithMessage("Event type is required.")
            .MaximumLength(100).WithMessage("Event type must not exceed 100 characters.");

        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("Entity type is required.")
            .MaximumLength(100).WithMessage("Entity type must not exceed 100 characters.");

        RuleFor(x => x.EntityId)
            .NotEmpty().WithMessage("Entity ID is required.")
            .MaximumLength(100).WithMessage("Entity ID must not exceed 100 characters.");

        RuleFor(x => x.ProjectId)
            .NotEmpty().WithMessage("Project ID is required.");

        RuleFor(x => x.PayloadJson)
            .NotEmpty().WithMessage("Payload JSON is required.");

        RuleFor(x => x)
            .Must(x => x.NotifySaasA || x.NotifySaasB)
            .WithMessage("At least one SaaS target (SaaS A or SaaS B) must be selected.");
    }
}
