using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Queries.GetCustomerEmsUsers;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Unit tests for <see cref="GetCustomerEmsUsersQueryHandler"/>.
/// Covers: EMS customer user list returned, non-EMS customer rejected,
/// tenant isolation, SuperAdmin bypass, EMS API error handling.
/// </summary>
public class GetCustomerEmsUsersQueryHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<IProjectRepository>  _projectRepoMock  = new();
    private readonly Mock<ISaasAClient>        _saasAClientMock  = new();
    private readonly Mock<ICurrentUserService> _userMock         = new();
    private readonly Mock<ILogger<GetCustomerEmsUsersQueryHandler>> _loggerMock = new();

    private GetCustomerEmsUsersQueryHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _projectRepoMock.Object,
        _saasAClientMock.Object,
        _userMock.Object,
        _loggerMock.Object);

    private void SetupAuthorizedUser(Guid projectId)
    {
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
    }

    private void SetupSuperAdmin()
    {
        _userMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
    }

    // ── Happy path — plain numeric LegacyId ──────────────────────────────────

    [Fact]
    public async Task Handle_EmsCustomerNumericLegacyId_ReturnsUserList()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "EMS Firma", LegacyId = "42"
        };
        var project = new Project { Id = projectId, EmsApiKey = "test-key" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        SetupAuthorizedUser(projectId);

        var emsUsers = new List<EmsCompanyUser>
        {
            new(UserId: "1", Name: "Ali", Surname: "Veli", Email: "ali@ems.com",
                Role: "Admin", LoginName: "ali.veli", Password: "pass123"),
            new(UserId: "2", Name: "Ayse", Surname: "Yılmaz", Email: "ayse@ems.com",
                Role: "User", LoginName: "ayse.yilmaz", Password: "abc456")
        };

        _saasAClientMock
            .Setup(c => c.GetCompanyUsersAsync("test-key", 42, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(emsUsers);

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value[0].UserId.Should().Be("1");
        result.Value[0].Name.Should().Be("Ali");
        result.Value[0].Role.Should().Be("Admin");
        result.Value[1].LoginName.Should().Be("ayse.yilmaz");
    }

    // ── Happy path — "SAASA-{n}" prefixed LegacyId ───────────────────────────

    [Fact]
    public async Task Handle_EmsCustomerSaasaPrefixedLegacyId_ReturnsUserList()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "SAASA Firma", LegacyId = "SAASA-7"
        };
        var project = new Project { Id = projectId, EmsApiKey = "ems-api-key" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        SetupAuthorizedUser(projectId);

        var emsUsers = new List<EmsCompanyUser>
        {
            new(UserId: "10", Name: "Test", Surname: "User", Email: "test@ems.com",
                Role: "User", LoginName: "testuser", Password: "p@ss")
        };

        _saasAClientMock
            .Setup(c => c.GetCompanyUsersAsync("ems-api-key", 7, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(emsUsers);

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value[0].UserId.Should().Be("10");
        result.Value[0].Email.Should().Be("test@ems.com");
    }

    // ── Empty list — valid EMS customer with no users ─────────────────────────

    [Fact]
    public async Task Handle_EmsCustomerWithNoUsers_ReturnsEmptyList()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId, LegacyId = "99"
        };
        var project = new Project { Id = projectId, EmsApiKey = null };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        SetupAuthorizedUser(projectId);

        _saasAClientMock
            .Setup(c => c.GetCompanyUsersAsync(null, 99, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<EmsCompanyUser>());

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    // ── Customer not found ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        SetupAuthorizedUser(Guid.NewGuid());

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(Guid.NewGuid()), CancellationToken.None);

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
            Id = Guid.NewGuid(), ProjectId = projectB, LegacyId = "5"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectA }); // user is in A, not B

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("yetki");
        _saasAClientMock.Verify(
            c => c.GetCompanyUsersAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never,
            "EMS API should not be called when tenant authorization fails");
    }

    // ── SuperAdmin bypass ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SuperAdmin_CanAccessAnyTenantEmsUsers()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId, LegacyId = "3"
        };
        var project = new Project { Id = projectId, EmsApiKey = "key" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        SetupSuperAdmin();

        _saasAClientMock
            .Setup(c => c.GetCompanyUsersAsync(It.IsAny<string?>(), 3, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<EmsCompanyUser>
            {
                new("1", "Super", "User", "su@crm.com", "Admin", "super", "pw")
            });

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
    }

    // ── Non-EMS customer: null LegacyId ──────────────────────────────────────

    [Fact]
    public async Task Handle_NullLegacyId_ReturnsFailure()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectId, LegacyId = null
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        SetupAuthorizedUser(projectId);

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS");
        _saasAClientMock.Verify(
            c => c.GetCompanyUsersAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ── Non-EMS customer: PC- prefix ─────────────────────────────────────────

    [Fact]
    public async Task Handle_LegacyIdWithPcPrefix_ReturnsFailure()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectId, LegacyId = "PC-123"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        SetupAuthorizedUser(projectId);

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS");
        _saasAClientMock.Verify(
            c => c.GetCompanyUsersAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ── Non-EMS customer: REZV- prefix ───────────────────────────────────────

    [Fact]
    public async Task Handle_LegacyIdWithRezvPrefix_ReturnsFailure()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectId, LegacyId = "REZV-55"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        SetupAuthorizedUser(projectId);

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS");
        _saasAClientMock.Verify(
            c => c.GetCompanyUsersAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ── EMS API throws exception ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_EmsApiThrows_ReturnsFailure()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId, LegacyId = "11"
        };
        var project = new Project { Id = projectId, EmsApiKey = "key" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        SetupAuthorizedUser(projectId);

        _saasAClientMock
            .Setup(c => c.GetCompanyUsersAsync(It.IsAny<string?>(), 11, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("EMS API unavailable"));

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS");
    }

    // ── DTO mapping verification ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidEmsResponse_MapsDtoFieldsCorrectly()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId, LegacyId = "20"
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId });

        SetupAuthorizedUser(projectId);

        var emsUsers = new List<EmsCompanyUser>
        {
            new(UserId: "999", Name: "Mehmet", Surname: "Demir",
                Email: "mehmet@test.com", Role: "Manager",
                LoginName: "mehmet.demir", Password: "secret99")
        };

        _saasAClientMock
            .Setup(c => c.GetCompanyUsersAsync(null, 20, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(emsUsers);

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerEmsUsersQuery(customerId), CancellationToken.None);

        // Assert — all fields mapped correctly from EmsCompanyUser → EmsCompanyUserDto
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value![0];
        dto.UserId.Should().Be("999");
        dto.Name.Should().Be("Mehmet");
        dto.Surname.Should().Be("Demir");
        dto.Email.Should().Be("mehmet@test.com");
        dto.Role.Should().Be("Manager");
        dto.LoginName.Should().Be("mehmet.demir");
        dto.Password.Should().Be("secret99");
    }
}
