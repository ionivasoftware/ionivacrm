using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Commands.PushCustomerToRezerval;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Unit tests for <see cref="PushCustomerToRezervalCommandHandler"/>.
/// Covers: create scenario (POST), update scenario (PUT), LegacyId persistence,
/// tenant isolation, missing API key, invalid base64 logo, API errors.
/// </summary>
public class PushCustomerToRezervalCommandHandlerTests
{
    private readonly Mock<ICustomerRepository>  _customerRepoMock = new();
    private readonly Mock<IProjectRepository>   _projectRepoMock  = new();
    private readonly Mock<ISaasBClient>         _saasBClientMock  = new();
    private readonly Mock<ICurrentUserService>  _userMock         = new();
    private readonly Mock<ILogger<PushCustomerToRezervalCommandHandler>> _loggerMock = new();

    private PushCustomerToRezervalCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _projectRepoMock.Object,
        _saasBClientMock.Object,
        _userMock.Object,
        _loggerMock.Object);

    private static PushCustomerToRezervalCommand BuildCommand(Guid customerId) =>
        new()
        {
            CustomerId      = customerId,
            Name            = "Test Firma",
            Title           = "Ltd. Şti.",
            Phone           = "05001234567",
            Email           = "info@test.com",
            TaxUnit         = "Test Vergi Dairesi",
            TaxNumber       = "1234567890",
            Address         = "Test Cad. No:1 İstanbul",
            AdminNameSurname = "Admin User",
            AdminLoginName  = "adminuser",
            AdminPassword   = "P@ssw0rd!",
            AdminEmail      = "admin@test.com",
            AdminPhone      = "05009876543"
        };

    private void SetupAuthorizedUser(Guid projectId)
    {
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
    }

    // ── Create scenario ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CustomerWithoutRezvLegacyId_CallsCreate_ReturnsDtoWithWasCreatedTrue()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "Test Firma", LegacyId = null
        };
        var project = new Project { Id = projectId, RezervAlApiKey = "rezerval-api-key" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        SetupAuthorizedUser(projectId);

        _saasBClientMock
            .Setup(c => c.CreateRezervalCompanyAsync(
                It.IsAny<RezervalCompanyFormData>(),
                "rezerval-api-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RezervalCreateCompanyResponse(CompanyId: 999, Message: "Created"));

        _customerRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.WasCreated.Should().BeTrue();
        result.Value.RezervalCompanyId.Should().Be(999);
        result.Value.LegacyId.Should().Be("REZV-999");
    }

    // ── Create: LegacyId is saved as "REZV-{companyId}" ─────────────────────

    [Fact]
    public async Task Handle_Create_PersistsLegacyIdAsRezvFormat()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "Firma", LegacyId = null // no REZV- prefix → create
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, RezervAlApiKey = "key" });

        SetupAuthorizedUser(projectId);

        _saasBClientMock
            .Setup(c => c.CreateRezervalCompanyAsync(
                It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RezervalCreateCompanyResponse(CompanyId: 123, Message: null));

        Customer? savedCustomer = null;
        _customerRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => savedCustomer = c)
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert — LegacyId is persisted in the Customer entity
        result.IsSuccess.Should().BeTrue();

        savedCustomer.Should().NotBeNull("UpdateAsync must be called to save LegacyId");
        savedCustomer!.LegacyId.Should().Be("REZV-123");
        savedCustomer.LegacyId.Should().StartWith("REZV-");

        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.Is<Customer>(c => c.LegacyId == "REZV-123"), It.IsAny<CancellationToken>()),
            Times.Once,
            "UpdateAsync must be called exactly once with LegacyId='REZV-123'");
    }

    // ── Create: numeric LegacyId (EMS) → still creates in Rezerval ───────────

    [Fact]
    public async Task Handle_CustomerWithNumericEmsLegacyId_CallsCreateNotUpdate()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "EMS Firma", LegacyId = "42" // numeric = EMS, not REZV-
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, RezervAlApiKey = "key" });

        SetupAuthorizedUser(projectId);

        _saasBClientMock
            .Setup(c => c.CreateRezervalCompanyAsync(
                It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RezervalCreateCompanyResponse(CompanyId: 50, Message: null));

        _customerRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert — Create called, Update NOT called
        result.IsSuccess.Should().BeTrue();
        result.Value!.WasCreated.Should().BeTrue();

        _saasBClientMock.Verify(
            c => c.CreateRezervalCompanyAsync(It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _saasBClientMock.Verify(
            c => c.UpdateRezervalCompanyAsync(It.IsAny<int>(), It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Update should not be called for non-REZV LegacyId");
    }

    // ── Update scenario ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CustomerWithRezvLegacyId_CallsUpdate_ReturnsDtoWithWasCreatedFalse()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "Mevcut Firma", LegacyId = "REZV-456"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, RezervAlApiKey = "key" });

        SetupAuthorizedUser(projectId);

        _saasBClientMock
            .Setup(c => c.UpdateRezervalCompanyAsync(
                456, It.IsAny<RezervalCompanyFormData>(), "key", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.WasCreated.Should().BeFalse();
        result.Value.RezervalCompanyId.Should().Be(456);
        result.Value.LegacyId.Should().Be("REZV-456");

        _saasBClientMock.Verify(
            c => c.UpdateRezervalCompanyAsync(456, It.IsAny<RezervalCompanyFormData>(), "key", It.IsAny<CancellationToken>()),
            Times.Once);
        _saasBClientMock.Verify(
            c => c.CreateRezervalCompanyAsync(It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Create should not be called when LegacyId starts with REZV-");
    }

    // ── Update: UpdateAsync NOT called (no LegacyId change on update) ─────────

    [Fact]
    public async Task Handle_UpdateScenario_DoesNotCallCustomerRepositoryUpdate()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "Firma", LegacyId = "REZV-789"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, RezervAlApiKey = "key" });

        SetupAuthorizedUser(projectId);

        _saasBClientMock
            .Setup(c => c.UpdateRezervalCompanyAsync(
                789, It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert — UpdateAsync on customer repo not needed (LegacyId already set)
        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Customer UpdateAsync should not be called on update scenario — LegacyId is already REZV-");
    }

    // ── Customer not found ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        // Act
        var result = await CreateHandler().Handle(
            BuildCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("bulunamadı");
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CustomerInOtherTenant_ReturnsFailure()
    {
        // Arrange
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        var customer = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectB,
            CompanyName = "Other Tenant Firma"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectA });

        // Act
        var result = await CreateHandler().Handle(
            BuildCommand(customer.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("yetki");

        _saasBClientMock.Verify(
            c => c.CreateRezervalCompanyAsync(It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _saasBClientMock.Verify(
            c => c.UpdateRezervalCompanyAsync(It.IsAny<int>(), It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Missing API key ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ProjectWithoutRezervAlApiKey_ReturnsFailure()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer { Id = customerId, ProjectId = projectId };
        var project  = new Project  { Id = projectId, RezervAlApiKey = null }; // no key

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        SetupAuthorizedUser(projectId);

        // Act
        var result = await CreateHandler().Handle(
            BuildCommand(customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("API");

        _saasBClientMock.Verify(
            c => c.CreateRezervalCompanyAsync(It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Invalid base64 logo ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvalidBase64Logo_ReturnsFailure()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer { Id = customerId, ProjectId = projectId };
        var project  = new Project  { Id = projectId, RezervAlApiKey = "key" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        SetupAuthorizedUser(projectId);

        var commandWithBadLogo = new PushCustomerToRezervalCommand
        {
            CustomerId   = customerId,
            Name         = "Test",
            Title        = "Ltd.",
            Phone        = "05001234567",
            Email        = "info@test.com",
            TaxUnit      = "VD",
            TaxNumber    = "123",
            Address      = "Adres",
            AdminNameSurname = "Admin",
            AdminLoginName   = "admin",
            AdminPassword    = "pass",
            AdminEmail       = "admin@test.com",
            AdminPhone       = "0500",
            LogoBase64   = "NOT-VALID-BASE64!!!@@@", // deliberately invalid
            LogoFileName = "logo.png"
        };

        // Act
        var result = await CreateHandler().Handle(commandWithBadLogo, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("base64");
    }

    // ── Create API throws exception ────────────────────────────────────────────

    [Fact]
    public async Task Handle_CreateApiThrows_ReturnsFailure()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer { Id = customerId, ProjectId = projectId, LegacyId = null };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, RezervAlApiKey = "key" });

        SetupAuthorizedUser(projectId);

        _saasBClientMock
            .Setup(c => c.CreateRezervalCompanyAsync(
                It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Rezerval API unavailable"));

        // Act
        var result = await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("oluşturulamadı");

        // LegacyId should NOT be saved when create fails
        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Update API throws exception ────────────────────────────────────────────

    [Fact]
    public async Task Handle_UpdateApiThrows_ReturnsFailure()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId, LegacyId = "REZV-321"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, RezervAlApiKey = "key" });

        SetupAuthorizedUser(projectId);

        _saasBClientMock
            .Setup(c => c.UpdateRezervalCompanyAsync(
                321, It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Rezerval 500 Internal Error"));

        // Act
        var result = await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("güncellenemedi");
    }

    // ── SuperAdmin can push any tenant's customer ──────────────────────────────

    [Fact]
    public async Task Handle_SuperAdmin_CanPushAnyTenantCustomer()
    {
        // Arrange
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId, LegacyId = null
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, RezervAlApiKey = "key" });

        _userMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no explicit project access

        _saasBClientMock
            .Setup(c => c.CreateRezervalCompanyAsync(
                It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RezervalCreateCompanyResponse(CompanyId: 77, Message: null));

        _customerRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.WasCreated.Should().BeTrue();
        result.Value.RezervalCompanyId.Should().Be(77);
    }
}
