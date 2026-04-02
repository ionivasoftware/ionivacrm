using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="InvoiceRepository"/> using an in-memory EF Core database.
/// Covers cross-project query (GetAllAsync), tenant isolation, and soft-delete filtering.
/// </summary>
public class InvoiceRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly InvoiceRepository _repository;

    public InvoiceRepositoryTests()
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(x => x.ProjectIds).Returns(new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, currentUserMock.Object);
        _repository = new InvoiceRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // ── GetAllAsync (cross-project) ───────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_NullProjectIds_ReturnsInvoicesFromAllProjects()
    {
        // Arrange
        var project1 = CreateProject();
        var project2 = CreateProject();
        var customer1 = CreateCustomer(project1.Id);
        var customer2 = CreateCustomer(project2.Id);

        _context.Projects.AddRange(project1, project2);
        _context.Customers.AddRange(customer1, customer2);
        await _context.SaveChangesAsync();

        _context.Invoices.AddRange(
            CreateInvoice(project1.Id, customer1.Id),
            CreateInvoice(project2.Id, customer2.Id));
        await _context.SaveChangesAsync();

        // Act
        var results = await _repository.GetAllAsync(null);

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_WithProjectIdFilter_ReturnsOnlyMatchingProject()
    {
        // Arrange
        var project1 = CreateProject();
        var project2 = CreateProject();
        var customer1 = CreateCustomer(project1.Id);
        var customer2 = CreateCustomer(project2.Id);

        _context.Projects.AddRange(project1, project2);
        _context.Customers.AddRange(customer1, customer2);
        await _context.SaveChangesAsync();

        _context.Invoices.AddRange(
            CreateInvoice(project1.Id, customer1.Id),
            CreateInvoice(project2.Id, customer2.Id));
        await _context.SaveChangesAsync();

        // Act
        var results = await _repository.GetAllAsync(new List<Guid> { project1.Id });

        // Assert
        results.Should().HaveCount(1);
        results[0].ProjectId.Should().Be(project1.Id);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesSoftDeletedInvoices()
    {
        // Arrange
        var project = CreateProject();
        var customer = CreateCustomer(project.Id);
        _context.Projects.Add(project);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var activeInvoice = CreateInvoice(project.Id, customer.Id);
        var deletedInvoice = CreateInvoice(project.Id, customer.Id);
        deletedInvoice.IsDeleted = true;

        _context.Invoices.AddRange(activeInvoice, deletedInvoice);
        await _context.SaveChangesAsync();

        // Act
        var results = await _repository.GetAllAsync(null);

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(activeInvoice.Id);
    }

    [Fact]
    public async Task GetAllAsync_EmptyProjectIdsList_ReturnsNoInvoices()
    {
        // Arrange
        var project = CreateProject();
        var customer = CreateCustomer(project.Id);
        _context.Projects.Add(project);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        _context.Invoices.Add(CreateInvoice(project.Id, customer.Id));
        await _context.SaveChangesAsync();

        // Act — empty list means "filter by nothing" → no matches
        var results = await _repository.GetAllAsync(new List<Guid>());

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_OrderedByIssueDateDescending()
    {
        // Arrange
        var project = CreateProject();
        var customer = CreateCustomer(project.Id);
        _context.Projects.Add(project);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var olderInvoice = CreateInvoice(project.Id, customer.Id, issueDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newerInvoice = CreateInvoice(project.Id, customer.Id, issueDate: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        _context.Invoices.AddRange(olderInvoice, newerInvoice);
        await _context.SaveChangesAsync();

        // Act
        var results = await _repository.GetAllAsync(null);

        // Assert — newest first
        results[0].Id.Should().Be(newerInvoice.Id);
        results[1].Id.Should().Be(olderInvoice.Id);
    }

    // ── GetByProjectIdAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetByProjectIdAsync_ReturnsOnlyInvoicesForGivenProject()
    {
        // Arrange
        var project1 = CreateProject();
        var project2 = CreateProject();
        var customer1 = CreateCustomer(project1.Id);
        var customer2 = CreateCustomer(project2.Id);

        _context.Projects.AddRange(project1, project2);
        _context.Customers.AddRange(customer1, customer2);
        await _context.SaveChangesAsync();

        _context.Invoices.AddRange(
            CreateInvoice(project1.Id, customer1.Id),
            CreateInvoice(project2.Id, customer2.Id));
        await _context.SaveChangesAsync();

        // Act
        var results = await _repository.GetByProjectIdAsync(project1.Id);

        // Assert
        results.Should().HaveCount(1);
        results[0].ProjectId.Should().Be(project1.Id);
    }

    [Fact]
    public async Task GetByProjectIdAsync_ExcludesSoftDeletedInvoices()
    {
        // Arrange
        var project = CreateProject();
        var customer = CreateCustomer(project.Id);
        _context.Projects.Add(project);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var active = CreateInvoice(project.Id, customer.Id);
        var deleted = CreateInvoice(project.Id, customer.Id);
        deleted.IsDeleted = true;

        _context.Invoices.AddRange(active, deleted);
        await _context.SaveChangesAsync();

        // Act
        var results = await _repository.GetByProjectIdAsync(project.Id);

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(active.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Project CreateProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Project",
        IsActive = true
    };

    private static Customer CreateCustomer(Guid projectId) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        CompanyName = "Test Co",
        Status = CustomerStatus.Active
    };

    private static Invoice CreateInvoice(Guid projectId, Guid customerId, DateTime? issueDate = null) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        CustomerId = customerId,
        Title = "Test Invoice",
        IssueDate = issueDate ?? DateTime.UtcNow,
        DueDate = DateTime.UtcNow.AddDays(30),
        Status = InvoiceStatus.Draft,
        LinesJson = "[]"
    };
}
