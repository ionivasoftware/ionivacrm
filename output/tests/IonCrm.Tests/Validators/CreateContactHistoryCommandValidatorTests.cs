using FluentValidation.TestHelper;
using IonCrm.Application.ContactHistory.Commands.CreateContactHistory;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.Validators;

public class CreateContactHistoryCommandValidatorTests
{
    private readonly CreateContactHistoryCommandValidator _validator = new();

    [Fact]
    public void Should_NotHaveError_When_CommandIsValid()
    {
        var command = new CreateContactHistoryCommand
        {
            CustomerId = Guid.NewGuid(),
            Type = ContactType.Call,
            Subject = "Follow-up call",
            ContactedAt = DateTime.UtcNow.AddHours(-1)
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_HaveError_When_CustomerIdIsEmpty()
    {
        var command = new CreateContactHistoryCommand
        {
            CustomerId = Guid.Empty,
            Type = ContactType.Email,
            ContactedAt = DateTime.UtcNow.AddHours(-1)
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.CustomerId);
    }

    [Fact]
    public void Should_HaveError_When_ContactedAtIsInTheFuture()
    {
        var command = new CreateContactHistoryCommand
        {
            CustomerId = Guid.NewGuid(),
            Type = ContactType.Meeting,
            ContactedAt = DateTime.UtcNow.AddHours(3) // more than 1 hour in the future
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.ContactedAt);
    }

    [Fact]
    public void Should_HaveError_When_SubjectIsTooLong()
    {
        var command = new CreateContactHistoryCommand
        {
            CustomerId = Guid.NewGuid(),
            Type = ContactType.Call,
            Subject = new string('A', 501), // exceeds 500 chars
            ContactedAt = DateTime.UtcNow.AddHours(-1)
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Subject);
    }
}
