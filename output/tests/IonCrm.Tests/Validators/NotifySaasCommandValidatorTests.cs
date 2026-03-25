using FluentValidation.TestHelper;
using IonCrm.Application.Features.Sync.Commands.NotifySaas;

namespace IonCrm.Tests.Validators;

/// <summary>
/// Tests for NotifySaasCommandValidator — outbound callback event validator.
/// Ensures at least one SaaS target is specified and all required fields present.
/// </summary>
public class NotifySaasCommandValidatorTests
{
    private readonly NotifySaasCommandValidator _validator = new();

    private static NotifySaasCommand Valid() => new(
        EventType: "status_changed",
        EntityType: "customer",
        EntityId: "CUST-001",
        ProjectId: Guid.NewGuid(),
        PayloadJson: "{\"status\":\"Active\"}",
        NotifySaasA: true,
        NotifySaasB: false);

    [Fact]
    public void Should_NotHaveErrors_When_CommandIsValid()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_HaveError_When_EventTypeIsEmpty()
    {
        var cmd = Valid() with { EventType = "" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.EventType);
    }

    [Fact]
    public void Should_HaveError_When_EntityTypeIsEmpty()
    {
        var cmd = Valid() with { EntityType = "" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.EntityType);
    }

    [Fact]
    public void Should_HaveError_When_EntityIdIsEmpty()
    {
        var cmd = Valid() with { EntityId = "" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.EntityId);
    }

    [Fact]
    public void Should_HaveError_When_ProjectIdIsEmpty()
    {
        var cmd = Valid() with { ProjectId = Guid.Empty };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public void Should_HaveError_When_PayloadJsonIsEmpty()
    {
        var cmd = Valid() with { PayloadJson = "" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PayloadJson);
    }

    [Fact]
    public void Should_HaveError_When_NeitherSaasSelected()
    {
        // The "must notify at least one SaaS" rule targets the entire command
        var cmd = Valid() with { NotifySaasA = false, NotifySaasB = false };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("At least one SaaS target (SaaS A or SaaS B) must be selected.");
    }

    [Fact]
    public void Should_NotHaveError_When_OnlySaasBSelected()
    {
        var cmd = Valid() with { NotifySaasA = false, NotifySaasB = true };
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_HaveError_When_EventTypeExceeds100Characters()
    {
        var cmd = Valid() with { EventType = new string('x', 101) };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.EventType);
    }
}
