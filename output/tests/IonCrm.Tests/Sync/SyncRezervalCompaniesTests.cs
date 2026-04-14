using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Unit tests for <see cref="SaasSyncJob.SyncRezervalCompaniesAsync"/> (via RunAsync).
/// Covers: new company insert, existing company update, LegacyId "REZV-{id}" format,
/// deleted companies skipped, and status computed from ExpirationDate.
/// </summary>
public class SyncRezervalCompaniesTests
{
    private readonly Mock<ISaasAClient>        _saasAClientMock  = new();
    private readonly Mock<ISaasBClient>        _saasBClientMock  = new();
    private readonly Mock<IProjectRepository>  _projectRepoMock  = new();
    private readonly Mock<ILogger<SaasSyncJob>> _loggerMock      = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IConfiguration>      _configMock       = new();
    private readonly Mock<IMediator>           _mediatorMock     = new();

    private SaasSyncJob CreateJob() => new(
        _saasAClientMock.Object,
        _saasBClientMock.Object,
        _projectRepoMock.Object,
        _scopeFactoryMock.Object,
        _configMock.Object,
        _loggerMock.Object,
        _mediatorMock.Object);

    private static ApplicationDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        return new ApplicationDbContext(options, currentUserMock.Object);
    }

    private void SetupScopeFactory(ApplicationDbContext dbContext)
    {
        var mockSyncLogRepo = new Mock<ISyncLogRepository>();
        mockSyncLogRepo
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);
        mockSyncLogRepo
            .Setup(r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(p => p.GetService(typeof(ISyncLogRepository)))
            .Returns(mockSyncLogRepo.Object);
        mockServiceProvider
            .Setup(p => p.GetService(typeof(ApplicationDbContext)))
            .Returns(dbContext);

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        _scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);
    }

    private void SetupNoEmsSync()
    {
        // Skip EMS sync — no SaasA config
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
    }

    // ── Insert: new company → customer created with REZV-{id} LegacyId ────────

    [Fact]
    public async Task SyncRezervalCompanies_NewCompany_InsertsCustomerWithRezvLegacyId()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_insert");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Rezerval Project", RezervAlApiKey = "rezerval-key"
        });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        var companies = new List<RezervalCompany>
        {
            new(
                Id: 101,
                Name: "Test Restoran",
                Title: "Tekil Restoran",
                Phone: "05001112233",
                Email: "test@restoran.com",
                Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null,
                ExperationDate: DateTime.UtcNow.AddDays(60),  // not expired, long trial → Active
                CreatedOn: DateTime.UtcNow.AddDays(-90),       // created 90d ago → createdAt+40 < expDate
                IsDeleted: false,
                IsActiveOnline: true)
        };

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync("rezerval-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(companies);

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        var customer = await dbContext.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.LegacyId == "REZV-101");

        customer.Should().NotBeNull("new Rezerval company should be inserted");
        customer!.LegacyId.Should().Be("REZV-101");
        customer.CompanyName.Should().Be("Test Restoran");
        customer.Email.Should().Be("test@restoran.com");
        customer.Phone.Should().Be("05001112233");
        customer.Segment.Should().BeNull("Title is ünvan, not segment — Rezerval sync no longer maps Title→Segment");
        customer.ProjectId.Should().Be(projectId);
    }

    // ── Insert: LegacyId format is exactly "REZV-{numericId}" ─────────────────

    [Fact]
    public async Task SyncRezervalCompanies_NewCompany_LegacyIdHasRezvPrefix()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_legacyid_format");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Test", RezervAlApiKey = "key"
        });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                new(Id: 777, Name: "Firma 777", Title: null, Phone: null, Email: null,
                    Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: DateTime.UtcNow.AddDays(30),
                    CreatedOn: DateTime.UtcNow.AddDays(-100), IsDeleted: false, IsActiveOnline: true)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert — LegacyId strictly matches "REZV-{id}"
        var customer = await dbContext.Customers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.ProjectId == projectId);

        customer.Should().NotBeNull();
        customer!.LegacyId.Should().Be("REZV-777");
        customer.LegacyId.Should().StartWith("REZV-");
        var numericPart = customer.LegacyId!["REZV-".Length..];
        numericPart.Should().Be("777", "numeric part of LegacyId must match source Id");
        int.TryParse(numericPart, out _).Should().BeTrue("numeric part must be parseable as int");
    }

    // ── Update: existing company → fields updated ─────────────────────────────

    [Fact]
    public async Task SyncRezervalCompanies_ExistingCompany_UpdatesChangedFields()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_update");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Rezerval Project", RezervAlApiKey = "key"
        });

        // Pre-existing customer with old data
        var existingCustomer = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = "REZV-200",
            CompanyName = "Eski Ad",
            Email = "eski@email.com",
            Phone = "050000000",
            Segment = "Eski Segment",
            ExpirationDate = DateTime.UtcNow.AddDays(-1),
            Status = CustomerStatus.Churned
        };
        dbContext.Customers.Add(existingCustomer);
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        var newExpDate = DateTime.UtcNow.AddDays(90);
        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                new(Id: 200, Name: "Yeni Ad", Title: "Yeni Segment",
                    Phone: "05559998877", Email: "yeni@email.com",
                    Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: newExpDate,
                    CreatedOn: DateTime.UtcNow.AddDays(-200),
                    IsDeleted: false, IsActiveOnline: true)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert — existing record is updated, not duplicated
        var allCustomers = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(c => c.LegacyId == "REZV-200")
            .ToListAsync();

        allCustomers.Should().HaveCount(1, "should update existing, not insert duplicate");

        var updated = allCustomers[0];
        updated.CompanyName.Should().Be("Yeni Ad");
        updated.Email.Should().Be("yeni@email.com");
        updated.Phone.Should().Be("05559998877");
        updated.Segment.Should().Be("Eski Segment",
            "Segment is a CRM-only field — Rezerval sync no longer touches it (Title is ünvan, not segment)");
    }

    // ── Update: no changes → UpdatedAt not bumped ─────────────────────────────

    [Fact]
    public async Task SyncRezervalCompanies_ExistingCompanyNoChanges_DoesNotDuplicate()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_no_change");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Test", RezervAlApiKey = "key"
        });

        var expDate = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Utc);
        var existingCustomer = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = "REZV-300",
            CompanyName = "Same Name",
            Email = "same@email.com",
            Phone = "05550001122",
            Segment = "Same Segment",
            ExpirationDate = expDate,
            Status = CustomerStatus.Active
        };
        dbContext.Customers.Add(existingCustomer);
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                new(Id: 300, Name: "Same Name", Title: "Same Segment",
                    Phone: "05550001122", Email: "same@email.com",
                    Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: expDate,
                    CreatedOn: DateTime.UtcNow.AddDays(-100), IsDeleted: false, IsActiveOnline: true)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert — still only one record
        var count = await dbContext.Customers
            .IgnoreQueryFilters()
            .CountAsync(c => c.LegacyId == "REZV-300");

        count.Should().Be(1, "identical data should not create a duplicate");
    }

    // ── Skip: deleted companies in source ─────────────────────────────────────

    [Fact]
    public async Task SyncRezervalCompanies_DeletedCompany_IsNotInserted()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_deleted");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Test", RezervAlApiKey = "key"
        });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                new(Id: 400, Name: "Deleted Firma", Title: null, Phone: null, Email: null,
                    Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: DateTime.UtcNow.AddDays(10),
                    CreatedOn: DateTime.UtcNow.AddDays(-50), IsDeleted: true, IsActiveOnline: false)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert — deleted companies must be skipped
        var customer = await dbContext.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.LegacyId == "REZV-400");

        customer.Should().BeNull("deleted companies should not be inserted into CRM");
    }

    // ── Status computation: Active (long trial, not expired) ─────────────────

    [Fact]
    public async Task SyncRezervalCompanies_LongTrialNotExpired_SetsStatusActive()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_active");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Test", RezervAlApiKey = "key"
        });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        // createdOn + 40d < expDate (long trial) AND today < expDate → Active
        var createdOn = DateTime.UtcNow.AddDays(-200);
        var expDate   = DateTime.UtcNow.AddDays(30);

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                new(Id: 500, Name: "Active Firma", Title: null, Phone: null, Email: null,
                    Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: expDate, CreatedOn: createdOn,
                    IsDeleted: false, IsActiveOnline: true)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        var customer = await dbContext.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.LegacyId == "REZV-500");

        customer.Should().NotBeNull();
        customer!.Status.Should().Be(CustomerStatus.Active);
    }

    // ── Status computation: Churned (long trial, expired) ────────────────────

    [Fact]
    public async Task SyncRezervalCompanies_LongTrialExpired_SetsStatusChurned()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_churned");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Test", RezervAlApiKey = "key"
        });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        // createdOn + 40d < expDate (long trial) AND expDate < today → Churned
        var createdOn = DateTime.UtcNow.AddDays(-200);
        var expDate   = DateTime.UtcNow.AddDays(-10); // already expired

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                new(Id: 501, Name: "Churned Firma", Title: null, Phone: null, Email: null,
                    Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: expDate, CreatedOn: createdOn,
                    IsDeleted: false, IsActiveOnline: false)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        var customer = await dbContext.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.LegacyId == "REZV-501");

        customer.Should().NotBeNull();
        customer!.Status.Should().Be(CustomerStatus.Churned);
    }

    // ── Status computation: Demo (short trial, not expired) ──────────────────

    [Fact]
    public async Task SyncRezervalCompanies_ShortTrialNotExpired_SetsStatusDemo()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_demo");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Test", RezervAlApiKey = "key"
        });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        // createdOn + 40d > expDate (short trial) AND today < expDate → Demo
        var createdOn = DateTime.UtcNow.AddDays(-10);
        var expDate   = DateTime.UtcNow.AddDays(20); // createdOn+40 = now+30 > expDate (now+20) ✓

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                new(Id: 502, Name: "Demo Firma", Title: null, Phone: null, Email: null,
                    Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: expDate, CreatedOn: createdOn,
                    IsDeleted: false, IsActiveOnline: true)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        var customer = await dbContext.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.LegacyId == "REZV-502");

        customer.Should().NotBeNull();
        customer!.Status.Should().Be(CustomerStatus.Demo);
    }

    // ── Multiple companies: mix of insert/update/skip ─────────────────────────

    [Fact]
    public async Task SyncRezervalCompanies_MixedBatch_InsertsNewAndUpdatesExisting()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns(projectId.ToString());

        var dbContext = CreateInMemoryDbContext("db_rezerval_mixed");
        dbContext.Projects.Add(new Project
        {
            Id = projectId, Name = "Test", RezervAlApiKey = "key"
        });

        // One existing record
        dbContext.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = "REZV-600",
            CompanyName = "Old Name",
            Email = "old@email.com"
        });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        _saasBClientMock
            .Setup(c => c.GetRezervalCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RezervalCompany>
            {
                // New company
                new(Id: 601, Name: "Brand New Co", Title: "New Segment", Phone: null,
                    Email: "new@co.com", Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: DateTime.UtcNow.AddDays(60),
                    CreatedOn: DateTime.UtcNow.AddDays(-100), IsDeleted: false, IsActiveOnline: true),
                // Existing — update
                new(Id: 600, Name: "Updated Name", Title: null, Phone: null,
                    Email: "updated@email.com", Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: DateTime.UtcNow.AddDays(60),
                    CreatedOn: DateTime.UtcNow.AddDays(-100), IsDeleted: false, IsActiveOnline: true),
                // Deleted — should be skipped
                new(Id: 602, Name: "Deleted Co", Title: null, Phone: null,
                    Email: null, Logo: null, TaxUnit: null, TaxNumber: null, TcNo: null, Address: null, ExperationDate: DateTime.UtcNow.AddDays(10),
                    CreatedOn: DateTime.UtcNow.AddDays(-50), IsDeleted: true, IsActiveOnline: false)
            });

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        var allCustomers = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(c => c.ProjectId == projectId)
            .ToListAsync();

        allCustomers.Should().HaveCount(2, "new + existing = 2; deleted skipped");
        allCustomers.Should().Contain(c => c.LegacyId == "REZV-601");
        allCustomers.Should().Contain(c => c.LegacyId == "REZV-600");
        allCustomers.Should().NotContain(c => c.LegacyId == "REZV-602");

        var updated = allCustomers.First(c => c.LegacyId == "REZV-600");
        updated.CompanyName.Should().Be("Updated Name");
        updated.Email.Should().Be("updated@email.com");
    }
}
