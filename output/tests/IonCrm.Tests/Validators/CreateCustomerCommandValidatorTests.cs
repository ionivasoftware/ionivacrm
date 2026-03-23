using FluentValidation.TestHelper;
using IonCrm.Application.Customers.Commands.CreateCustomer;

namespace IonCrm.Tests.Validators;

public class CreateCustomerCommandValidatorTests
{
    private readonly CreateCustomerCommandValidator _validator = new();

    [Fact]
    public void Should_HaveError_When_CompanyNameIsEmpty()
    {
        var command = new CreateCustomerCommand { ProjectId = Guid.NewGuid(), CompanyName = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.CompanyName);
    }

    [Fact]
    public void Should_HaveError_When_ProjectIdIsEmpty()
    {
        var command = new CreateCustomerCommand { ProjectId = Guid.Empty, CompanyName = "Test" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.ProjectId);
    }

    [Fact]
    public void Should_HaveError_When_EmailIsInvalid()
    {
        var command = new CreateCustomerCommand
        {
            ProjectId = Guid.NewGuid(),
            CompanyName = "Test",
            Email = "not-an-email"
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Should_NotHaveError_When_CommandIsValid()
    {
        var command = new CreateCustomerCommand
        {
            ProjectId = Guid.NewGuid(),
            CompanyName = "Acme Corp",
            Email = "contact@acme.com",
            Phone = "555-1234"
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_NotHaveError_When_OptionalFieldsAreNull()
    {
        var command = new CreateCustomerCommand
        {
            ProjectId = Guid.NewGuid(),
            CompanyName = "Minimal Corp"
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
