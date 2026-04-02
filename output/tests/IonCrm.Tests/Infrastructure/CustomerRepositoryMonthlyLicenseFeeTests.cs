using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Tests.Infrastructure;

/// <summary>
/// Unit tests verifying that <c>Customer.MonthlyLicenseFee</c> is correctly persisted and retrieved
/// by <see cref="CustomerRepository"/>.
/// This field is used for RezervAl customers' individual monthly license fees.
/// </summary>
public class CustomerRepositoryMonthlyLicenseFeeTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly CustomerRepository _repository;
    private readonly Guid _projectId = Guid.NewGuid();

    public CustomerRepositoryMonthlyLicenseFeeTests()
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(x => x.ProjectIds).Returns(new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, currentUserMock.Object);
        _repository = new CustomerRepository(_context);

        // Seed a project (FK constraint)
        _context.Projects.Add(new Project { Id = _projectId, Name = "RezervAl Projesi", IsActive = true });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    // ── MonthlyLicenseFee persistence ────────────────────────────────────────

    [Fact]
    public async Task AddAsync_WithMonthlyLicenseFee_PersistsValue()
    {
        // Arrange
        var customer = CreateRezervalCustomer(monthlyFee: 299.99m);

        // Act
        await _repository.AddAsync(customer);

        // Assert
        var persisted = await _context.Customers.FindAsync(customer.Id);
        persisted.Should().NotBeNull();
        persisted!.MonthlyLicenseFee.Should().Be(299.99m);
    }

    [Fact]
    public async Task AddAsync_WithNullMonthlyLicenseFee_PersistsNull()
    {
        // Arrange
        var customer = CreateRezervalCustomer(monthlyFee: null);

        // Act
        await _repository.AddAsync(customer);

        // Assert
        var persisted = await _context.Customers.FindAsync(customer.Id);
        persisted!.MonthlyLicenseFee.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCustomerWithCorrectMonthlyLicenseFee()
    {
        // Arrange
        var customer = CreateRezervalCustomer(monthlyFee: 599.00m);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(customer.Id);

        // Assert
        result.Should().NotBeNull();
        result!.MonthlyLicenseFee.Should().Be(599.00m);
    }

    [Fact]
    public async Task UpdateAsync_ChangesMonthlyLicenseFee()
    {
        // Arrange
        var customer = CreateRezervalCustomer(monthlyFee: 100.00m);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        // Act — update the fee
        customer.MonthlyLicenseFee = 250.50m;
        await _repository.UpdateAsync(customer);

        // Assert
        var updated = await _context.Customers.FindAsync(customer.Id);
        updated!.MonthlyLicenseFee.Should().Be(250.50m);
    }

    [Fact]
    public async Task UpdateAsync_ClearsMonthlyLicenseFee_WhenSetToNull()
    {
        // Arrange
        var customer = CreateRezervalCustomer(monthlyFee: 150.00m);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        // Act — clear the fee
        customer.MonthlyLicenseFee = null;
        await _repository.UpdateAsync(customer);

        // Assert
        var updated = await _context.Customers.FindAsync(customer.Id);
        updated!.MonthlyLicenseFee.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedAsync_CustomersReturnedIncludeMonthlyLicenseFee()
    {
        // Arrange
        var customerWithFee = CreateRezervalCustomer(monthlyFee: 399.00m);
        customerWithFee.CompanyName = "RezervAl Restoran";
        _context.Customers.Add(customerWithFee);
        await _context.SaveChangesAsync();

        // Act
        var (items, _) = await _repository.GetPagedAsync(
            _projectId, null, null, null, null, null, 1, 10);

        // Assert
        items.Should().ContainSingle();
        items[0].MonthlyLicenseFee.Should().Be(399.00m);
    }

    [Fact]
    public async Task GetWithDetailsAsync_IncludesMonthlyLicenseFee()
    {
        // Arrange
        var customer = CreateRezervalCustomer(monthlyFee: 750.00m);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetWithDetailsAsync(customer.Id);

        // Assert
        result.Should().NotBeNull();
        result!.MonthlyLicenseFee.Should().Be(750.00m);
    }

    // ── Multiple customers — isolation ────────────────────────────────────────

    [Fact]
    public async Task MultipleCustomers_EachHaveIndependentMonthlyLicenseFee()
    {
        // Arrange — each RezervAl customer has their own fee
        var customer1 = CreateRezervalCustomer(monthlyFee: 100m);
        customer1.CompanyName = "Restoran A";
        var customer2 = CreateRezervalCustomer(monthlyFee: 200m);
        customer2.CompanyName = "Restoran B";
        var customer3 = CreateRezervalCustomer(monthlyFee: null);
        customer3.CompanyName = "Restoran C";

        _context.Customers.AddRange(customer1, customer2, customer3);
        await _context.SaveChangesAsync();

        // Act
        var c1 = await _repository.GetByIdAsync(customer1.Id);
        var c2 = await _repository.GetByIdAsync(customer2.Id);
        var c3 = await _repository.GetByIdAsync(customer3.Id);

        // Assert
        c1!.MonthlyLicenseFee.Should().Be(100m);
        c2!.MonthlyLicenseFee.Should().Be(200m);
        c3!.MonthlyLicenseFee.Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Customer CreateRezervalCustomer(decimal? monthlyFee) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = _projectId,
        CompanyName = "Rezerval Müşteri",
        Status = CustomerStatus.Active,
        LegacyId = $"REZV-{Random.Shared.Next(1000, 9999)}",
        MonthlyLicenseFee = monthlyFee
    };
}
