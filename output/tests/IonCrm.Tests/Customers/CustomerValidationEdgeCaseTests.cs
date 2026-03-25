using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Additional edge-case tests for Customer command handlers:
/// - Soft delete sets IsDeleted via repository (verified via mock)
/// - Segment + Status mappings in create/update
/// - Null optional fields handled without exception
/// </summary>
public class CustomerValidationEdgeCaseTests
{
    private readonly Mock<ICustomerRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();
    private readonly Mock<ILogger<CreateCustomerCommandHandler>> _createLoggerMock = new();
    private readonly Mock<ILogger<UpdateCustomerCommandHandler>> _updateLoggerMock = new();
    private readonly Mock<ILogger<DeleteCustomerCommandHandler>> _deleteLoggerMock = new();

    private CreateCustomerCommandHandler CreateHandler() => new(
        _repoMock.Object, _userMock.Object, _createLoggerMock.Object);

    private UpdateCustomerCommandHandler UpdateHandler() => new(
        _repoMock.Object, _userMock.Object, _updateLoggerMock.Object);

    private DeleteCustomerCommandHandler DeleteHandler() => new(
        _repoMock.Object, _userMock.Object, _deleteLoggerMock.Object);

    private Guid SetupAuthorizedUser()
    {
        var projectId = Guid.NewGuid();
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        return projectId;
    }

    // ── Create — segment and status default values ────────────────────────────

    [Fact]
    public async Task Create_WithNoSegment_SegmentIsNull()
    {
        // Arrange
        var projectId = SetupAuthorizedUser();
        Customer? added = null;

        _repoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = projectId,
            CompanyName = "No Segment Corp"
            // Segment not set
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        added!.Segment.Should().BeNull();
    }

    [Fact]
    public async Task Create_DefaultStatus_IsLead()
    {
        // Arrange
        var projectId = SetupAuthorizedUser();
        Customer? added = null;

        _repoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = projectId,
            CompanyName = "Default Status Corp"
            // Status not set — should default to Lead
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // The handler doesn't override Status; the entity default is CustomerStatus.Lead
        added!.Status.Should().Be(CustomerStatus.Lead);
    }

    [Fact]
    public async Task Create_AllOptionalFieldsNull_Succeeds()
    {
        // Arrange
        var projectId = SetupAuthorizedUser();

        _repoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = projectId,
            CompanyName = "Minimal Corp",
            ContactName = null,
            Email = null,
            Phone = null,
            Address = null,
            TaxNumber = null,
            TaxUnit = null,
            Code = null,
            AssignedUserId = null
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("Minimal Corp");
    }

    // ── Create — assigned user mapping ────────────────────────────────────────

    [Fact]
    public async Task Create_WithAssignedUser_MapsAssignedUserId()
    {
        // Arrange
        var projectId = SetupAuthorizedUser();
        var assignedUserId = Guid.NewGuid();
        Customer? added = null;

        _repoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = projectId,
            CompanyName = "Assigned Corp",
            AssignedUserId = assignedUserId
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        added!.AssignedUserId.Should().Be(assignedUserId);
    }

    // ── Delete — verifies DeleteAsync is called exactly once ─────────────────

    [Fact]
    public async Task Delete_AuthorizedUser_CallsDeleteAsyncExactlyOnce()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectId, CompanyName = "To Delete"
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _repoMock
            .Setup(r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await DeleteHandler().Handle(
            new DeleteCustomerCommand(customer.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repoMock.Verify(
            r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Delete_AccessDenied_DeleteAsyncNeverCalled()
    {
        // Arrange
        var customer = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), CompanyName = "Guarded Corp"
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds)
            .Returns(new List<Guid> { Guid.NewGuid() }); // different project

        // Act
        var result = await DeleteHandler().Handle(
            new DeleteCustomerCommand(customer.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _repoMock.Verify(
            r => r.DeleteAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Update — field mapping completeness ───────────────────────────────────

    [Fact]
    public async Task Update_AllFieldsMapped_IncludingTaxAndAddress()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectId, CompanyName = "Old Corp"
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _repoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand
        {
            Id = customer.Id,
            CompanyName = "New Corp",
            ContactName = "Jane Doe",
            Email = "jane@newcorp.com",
            Phone = "777-1234",
            Address = "42 New Street",
            TaxNumber = "TX-9999",
            TaxUnit = "Main Tax Office",
            Code = "NEW-01",
            Segment = CustomerSegment.Enterprise,
            Status = CustomerStatus.Active,
            AssignedUserId = Guid.NewGuid()
        };

        // Act
        var result = await UpdateHandler().Handle(command, CancellationToken.None);

        // Assert — every field mutated on the domain entity
        result.IsSuccess.Should().BeTrue();
        customer.CompanyName.Should().Be("New Corp");
        customer.ContactName.Should().Be("Jane Doe");
        customer.Email.Should().Be("jane@newcorp.com");
        customer.Phone.Should().Be("777-1234");
        customer.Address.Should().Be("42 New Street");
        customer.TaxNumber.Should().Be("TX-9999");
        customer.TaxUnit.Should().Be("Main Tax Office");
        customer.Code.Should().Be("NEW-01");
        customer.Segment.Should().Be(CustomerSegment.Enterprise);
        customer.Status.Should().Be(CustomerStatus.Active);
        customer.AssignedUserId.Should().Be(command.AssignedUserId);
    }

    // ── DTO field coverage ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsDto_WithAllExpectedFields()
    {
        // Arrange
        var projectId = SetupAuthorizedUser();
        var assignedUserId = Guid.NewGuid();

        _repoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = projectId,
            CompanyName = "DTO Test Corp",
            ContactName = "DTO Contact",
            Email = "dto@test.com",
            Phone = "111-2222",
            Address = "DTO Street",
            TaxNumber = "DTO-TAX",
            Status = CustomerStatus.Active,
            Segment = CustomerSegment.SME,
            AssignedUserId = assignedUserId
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.CompanyName.Should().Be("DTO Test Corp");
        dto.ContactName.Should().Be("DTO Contact");
        dto.Email.Should().Be("dto@test.com");
        dto.Phone.Should().Be("111-2222");
        dto.Address.Should().Be("DTO Street");
        dto.TaxNumber.Should().Be("DTO-TAX");
        dto.Status.Should().Be(CustomerStatus.Active);
        dto.Segment.Should().Be(CustomerSegment.SME);
        dto.AssignedUserId.Should().Be(assignedUserId);
        dto.ProjectId.Should().Be(projectId);
        dto.Id.Should().NotBe(Guid.Empty);
    }
}
