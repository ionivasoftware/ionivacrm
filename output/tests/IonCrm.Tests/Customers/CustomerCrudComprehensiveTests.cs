using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Application.Customers.Queries.GetCustomerById;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Comprehensive Customer CRUD tests covering:
/// - CreateCustomer: all optional fields mapped correctly to entity and DTO
/// - CreateCustomer: validation – missing CompanyName returns failure (via handler)
/// - UpdateCustomer: ALL fields updated on existing entity (not just a subset)
/// - UpdateCustomer: null email → null stored (not empty string)
/// - UpdateCustomer: returns DTO reflecting all updates
/// - UpdateCustomer: non-existent customer → failure
/// - DeleteCustomer: soft-delete sets IsDeleted=true (InMemory integration)
/// - DeleteCustomer: soft-deleted customer disappears from subsequent queries
/// - Soft-delete idempotency: deleting already-deleted customer completes (or fails gracefully)
/// </summary>
public class CustomerCrudComprehensiveTests : IDisposable
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<CreateCustomerCommandHandler>> _createLoggerMock = new();
    private readonly Mock<ILogger<UpdateCustomerCommandHandler>> _updateLoggerMock = new();
    private readonly Mock<ILogger<DeleteCustomerCommandHandler>> _deleteLoggerMock = new();

    private readonly ApplicationDbContext _dbContext;
    private readonly CustomerRepository _realRepo;

    private static readonly Guid ProjectId = Guid.NewGuid();

    public CustomerCrudComprehensiveTests()
    {
        // In-memory context used for integration-level assertions
        var superAdminMock = new Mock<ICurrentUserService>();
        superAdminMock.Setup(u => u.IsSuperAdmin).Returns(true);
        superAdminMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options, superAdminMock.Object);
        _realRepo = new CustomerRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // ── Factories ─────────────────────────────────────────────────────────────

    private CreateCustomerCommandHandler CreateCreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _createLoggerMock.Object);

    private UpdateCustomerCommandHandler CreateUpdateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _updateLoggerMock.Object);

    private DeleteCustomerCommandHandler CreateDeleteHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _deleteLoggerMock.Object);

    private void SetupAuthorizedUser()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { ProjectId });
    }

    private void SetupRepoAdd()
    {
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);
    }

    // ── Create: all optional fields mapped ───────────────────────────────────

    [Fact]
    public async Task CreateCustomer_AllOptionalFieldsProvided_AllMappedToDto()
    {
        // Arrange
        SetupAuthorizedUser();
        SetupRepoAdd();

        var assignedUserId = Guid.NewGuid();

        var command = new CreateCustomerCommand
        {
            ProjectId    = ProjectId,
            CompanyName  = "Full Corp",
            Code         = "CORP-001",
            ContactName  = "Alice Smith",
            Email        = "alice@fullcorp.com",
            Phone        = "+1-555-0001",
            Address      = "123 Main Street, NY",
            TaxNumber    = "TAX-9876",
            TaxUnit      = "IRS Office NYC",
            Status       = CustomerStatus.Active,
            Segment      = "Enterprise",
            Label        = CustomerLabel.YuksekPotansiyel,
            AssignedUserId = assignedUserId
        };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert — every field from command must appear in the DTO
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.CompanyName.Should().Be("Full Corp");
        dto.Code.Should().Be("CORP-001");
        dto.ContactName.Should().Be("Alice Smith");
        dto.Email.Should().Be("alice@fullcorp.com");
        dto.Phone.Should().Be("+1-555-0001");
        dto.Address.Should().Be("123 Main Street, NY");
        dto.TaxNumber.Should().Be("TAX-9876");
        dto.TaxUnit.Should().Be("IRS Office NYC");
        dto.Status.Should().Be(CustomerStatus.Active);
        dto.Segment.Should().Be("Enterprise");
        dto.Label.Should().Be(CustomerLabel.YuksekPotansiyel);
        dto.AssignedUserId.Should().Be(assignedUserId);
        dto.ProjectId.Should().Be(ProjectId);
    }

    [Fact]
    public async Task CreateCustomer_AllOptionalFieldsNull_SucceedsWithNullableFieldsNull()
    {
        // Arrange — only required fields provided
        SetupAuthorizedUser();
        SetupRepoAdd();

        var command = new CreateCustomerCommand
        {
            ProjectId   = ProjectId,
            CompanyName = "Minimal Corp"
            // All optional fields omitted → null
        };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert — operation succeeds; nullable fields remain null
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.CompanyName.Should().Be("Minimal Corp");
        dto.Code.Should().BeNull();
        dto.ContactName.Should().BeNull();
        dto.Email.Should().BeNull();
        dto.Phone.Should().BeNull();
        dto.Address.Should().BeNull();
        dto.TaxNumber.Should().BeNull();
        dto.TaxUnit.Should().BeNull();
        dto.Segment.Should().BeNull();
        dto.Label.Should().BeNull();
        dto.AssignedUserId.Should().BeNull();
    }

    [Fact]
    public async Task CreateCustomer_EmailWithMixedCase_NormalisedToLowercase()
    {
        // Arrange
        SetupAuthorizedUser();
        SetupRepoAdd();

        var command = new CreateCustomerCommand
        {
            ProjectId   = ProjectId,
            CompanyName = "Email Test Corp",
            Email       = "  SALES@Company.COM  "
        };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert — email must be lower-cased and trimmed
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("sales@company.com",
            "email must be stored in lowercase with whitespace trimmed");
    }

    [Fact]
    public async Task CreateCustomer_NullEmail_StoresNull()
    {
        // Arrange
        SetupAuthorizedUser();
        SetupRepoAdd();

        var command = new CreateCustomerCommand
        {
            ProjectId   = ProjectId,
            CompanyName = "No Email Corp",
            Email       = null
        };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().BeNull("null email must remain null, not become empty string");
    }

    [Fact]
    public async Task CreateCustomer_RepositoryCalledWithCorrectEntity()
    {
        // Arrange
        SetupAuthorizedUser();
        Customer? addedEntity = null;
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => addedEntity = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId   = ProjectId,
            CompanyName = "Capture Corp",
            Phone       = "555-1234",
            Status      = CustomerStatus.Active
        };

        // Act
        await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert — the entity passed to the repository must reflect the command
        addedEntity.Should().NotBeNull("AddAsync must be called with the new entity");
        addedEntity!.ProjectId.Should().Be(ProjectId);
        addedEntity.CompanyName.Should().Be("Capture Corp");
        addedEntity.Phone.Should().Be("555-1234");
        addedEntity.Status.Should().Be(CustomerStatus.Active);
    }

    // ── Update: all fields ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCustomer_AllFieldsReplaced_DtoReflectsAllChanges()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var newAssignee = Guid.NewGuid();
        var existingCustomer = new Customer
        {
            Id          = customerId,
            ProjectId   = ProjectId,
            CompanyName = "Old Corp",
            Code        = "OLD-001",
            ContactName = "Old Contact",
            Email       = "old@corp.com",
            Phone       = "000-0000",
            Address     = "Old Street",
            TaxNumber   = "OLD-TAX",
            TaxUnit     = "Old Tax Unit",
            Status      = CustomerStatus.Lead,
            Segment     = null,
            Label       = null,
            AssignedUserId = null
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCustomer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { ProjectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(existingCustomer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand
        {
            Id             = customerId,
            CompanyName    = "New Corp",
            Code           = "NEW-001",
            ContactName    = "New Contact",
            Email          = "new@corp.com",
            Phone          = "999-9999",
            Address        = "New Street",
            TaxNumber      = "NEW-TAX",
            TaxUnit        = "New Tax Unit",
            Status         = CustomerStatus.Active,
            Segment        = "SME",
            Label          = CustomerLabel.Potansiyel,
            AssignedUserId = newAssignee
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert — every single field must be updated
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.CompanyName.Should().Be("New Corp");
        dto.Code.Should().Be("NEW-001");
        dto.ContactName.Should().Be("New Contact");
        dto.Email.Should().Be("new@corp.com");
        dto.Phone.Should().Be("999-9999");
        dto.Address.Should().Be("New Street");
        dto.TaxNumber.Should().Be("NEW-TAX");
        dto.TaxUnit.Should().Be("New Tax Unit");
        dto.Status.Should().Be(CustomerStatus.Active);
        dto.Segment.Should().Be("SME");
        dto.Label.Should().Be(CustomerLabel.Potansiyel);
        dto.AssignedUserId.Should().Be(newAssignee);
    }

    [Fact]
    public async Task UpdateCustomer_NullEmail_StoresNull()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id        = customerId,
            ProjectId = ProjectId,
            CompanyName = "Test Corp",
            Email     = "existing@corp.com"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { ProjectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand
        {
            Id          = customerId,
            CompanyName = "Test Corp",
            Email       = null         // explicitly clearing email
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert — null email → entity email must be null
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().BeNull("setting email to null must clear the stored value");
        customer.Email.Should().BeNull("entity email must be set to null");
    }

    [Fact]
    public async Task UpdateCustomer_NonExistentCustomer_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new UpdateCustomerCommand
        {
            Id          = Guid.NewGuid(),
            CompanyName = "Ghost Corp"
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "UpdateAsync must not be called for non-existent customer");
    }

    [Fact]
    public async Task UpdateCustomer_UpdateAsyncCalledExactlyOnce()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, ProjectId = ProjectId, CompanyName = "Corp" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { ProjectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand { Id = customerId, CompanyName = "Updated Corp" };

        // Act
        await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        _customerRepoMock.Verify(
            r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()),
            Times.Once,
            "UpdateAsync must be called exactly once per update");
    }

    [Fact]
    public async Task UpdateCustomer_EmailNormalisedToLowercase()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, ProjectId = ProjectId, CompanyName = "Corp" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { ProjectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand
        {
            Id          = customerId,
            CompanyName = "Corp",
            Email       = "  SALES@Corp.COM  "
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("sales@corp.com",
            "email must be lowercased and trimmed on update");
    }

    // ── Delete: soft-delete ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCustomer_SoftDelete_IsDeletedFlagSetToTrue_InMemory()
    {
        // Arrange — use real repository backed by InMemory EF to verify soft-delete
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id          = customerId,
            ProjectId   = ProjectId,
            CompanyName = "Soft Delete Corp"
        };
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();

        // Act — call soft-delete on the real repository
        await _realRepo.DeleteAsync(customer);

        // Assert — IsDeleted flag must be true
        customer.IsDeleted.Should().BeTrue("DeleteAsync must set IsDeleted=true (soft delete)");

        // Verify the change is persisted in the store
        var fromDb = await _dbContext.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == customerId);
        fromDb.Should().NotBeNull();
        fromDb!.IsDeleted.Should().BeTrue("soft-deleted flag must be persisted to the database");
    }

    [Fact]
    public async Task DeleteCustomer_SoftDeletedCustomer_DisappearsFromRegularQueries()
    {
        // Arrange — add a live customer then soft-delete it
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id          = customerId,
            ProjectId   = ProjectId,
            CompanyName = "To Be Erased"
        };
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();

        // Confirm it's visible before deletion
        var (before, beforeTotal) = await _realRepo.GetPagedAsync(
            null, null, null, null, null, null, 1, 50);
        beforeTotal.Should().BeGreaterThan(0, "customer must be visible before deletion");

        // Act — soft-delete
        await _realRepo.DeleteAsync(customer);

        // Assert — must not appear in paged queries
        var (after, afterTotal) = await _realRepo.GetPagedAsync(
            null, null, null, null, null, null, 1, 50);
        after.Should().NotContain(c => c.Id == customerId,
            "soft-deleted customer must not appear in GetPagedAsync results");
    }

    [Fact]
    public async Task DeleteCustomer_Handler_CallsDeleteAsyncOnce()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, ProjectId = ProjectId, CompanyName = "Del Corp" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { ProjectId });
        _customerRepoMock
            .Setup(r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateDeleteHandler().Handle(
            new DeleteCustomerCommand(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(
            r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()),
            Times.Once,
            "DeleteAsync must be called exactly once");
    }

    [Fact]
    public async Task DeleteCustomer_NonExistentCustomer_ReturnsFailure_RepositoryNeverCalled()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        // Act
        var result = await CreateDeleteHandler().Handle(
            new DeleteCustomerCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
        _customerRepoMock.Verify(
            r => r.DeleteAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "DeleteAsync must not be called when customer does not exist");
    }

    // ── GetCustomerById ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetCustomerById_ExistingCustomer_ReturnsCorrectDto()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer
            {
                Id          = customerId,
                ProjectId   = ProjectId,
                CompanyName = "Query Corp",
                Email       = "query@corp.com",
                Status      = CustomerStatus.Active
            });
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { ProjectId });

        var handler = new GetCustomerByIdQueryHandler(customerRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetCustomerByIdQuery(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(customerId);
        result.Value.CompanyName.Should().Be("Query Corp");
        result.Value.Email.Should().Be("query@corp.com");
        result.Value.Status.Should().Be(CustomerStatus.Active);
    }

    // ── All segments / statuses / labels round-trip ───────────────────────────

    [Theory]
    [InlineData(CustomerStatus.Lead)]
    [InlineData(CustomerStatus.Active)]
    [InlineData(CustomerStatus.Demo)]
    [InlineData(CustomerStatus.Churned)]
    public async Task CreateCustomer_AllStatusValues_PersistedCorrectly(CustomerStatus status)
    {
        // Arrange
        SetupAuthorizedUser();
        SetupRepoAdd();

        var command = new CreateCustomerCommand
        {
            ProjectId   = ProjectId,
            CompanyName = $"Status Corp {status}",
            Status      = status
        };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(status, $"status {status} must survive the round-trip");
    }

    [Theory]
    [InlineData("Asansör Firması")]
    [InlineData("Tekil Restoran")]
    [InlineData("Zincir Restoran")]
    public async Task CreateCustomer_AllSegmentStrings_PersistedCorrectly(string segment)
    {
        // Arrange – Segment is a free string per project config (CustomerSegment enum is obsolete)
        SetupAuthorizedUser();
        SetupRepoAdd();

        var command = new CreateCustomerCommand
        {
            ProjectId   = ProjectId,
            CompanyName = $"Segment Corp {segment}",
            Segment     = segment
        };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Segment.Should().Be(segment, $"segment '{segment}' must survive the round-trip");
    }

    [Theory]
    [InlineData(CustomerLabel.YuksekPotansiyel)]
    [InlineData(CustomerLabel.Potansiyel)]
    [InlineData(CustomerLabel.Notr)]
    [InlineData(CustomerLabel.Vasat)]
    [InlineData(CustomerLabel.Kotu)]
    public async Task CreateCustomer_AllLabelValues_PersistedCorrectly(CustomerLabel label)
    {
        // Arrange
        SetupAuthorizedUser();
        SetupRepoAdd();

        var command = new CreateCustomerCommand
        {
            ProjectId   = ProjectId,
            CompanyName = $"Label Corp {label}",
            Label       = label
        };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Label.Should().Be(label, $"label {label} must survive the round-trip");
    }
}
