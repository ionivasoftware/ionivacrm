using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Queries.GetCustomerEmsSummary;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Unit tests for <see cref="GetCustomerEmsSummaryQueryHandler"/>.
/// Covers: authorization, LegacyId validation, EMS API proxy, DTO mapping.
/// </summary>
public class GetCustomerEmsSummaryQueryHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<IProjectRepository> _projectRepoMock = new();
    private readonly Mock<ISaasAClient> _saasAClientMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<GetCustomerEmsSummaryQueryHandler>> _loggerMock = new();

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    private GetCustomerEmsSummaryQueryHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _projectRepoMock.Object,
        _saasAClientMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private void SetupAuthorizedUser()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
    }

    private Customer CreateEmsCustomer(string legacyId) => new()
    {
        Id = _customerId,
        ProjectId = _projectId,
        CompanyName = "EMS Müşterisi A.Ş.",
        Status = CustomerStatus.Active,
        LegacyId = legacyId
    };

    private static EmsCompanySummaryResponse CreateEmsResponse(int emsCompanyId = 42) =>
        new(emsCompanyId,
            new EmsCompanySummaryTotals(10, 25, 5),
            new List<EmsCompanyMonthlyStat>
            {
                new(2026, 1, 15, 3, 2),
                new(2026, 2, 12, 1, 4)
            });

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("bulunamadı");
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsCustomer("42"));

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("erişim");
    }

    // ── LegacyId validation ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CustomerWithNullLegacyId_ReturnsFailure()
    {
        // Arrange
        SetupAuthorizedUser();
        var customer = CreateEmsCustomer(null!);
        customer.LegacyId = null;
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS kaynaklı değil");
    }

    [Theory]
    [InlineData("PC-123")]        // PotentialCustomer — not EMS
    [InlineData("REZV-456")]      // RezervAl — not EMS
    [InlineData("PC-0")]
    [InlineData("rezv-1")]        // case-insensitive check
    public async Task Handle_NonEmsLegacyId_ReturnsFailure(string legacyId)
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsCustomer(legacyId));

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS kaynaklı değil");
    }

    [Theory]
    [InlineData("42")]           // plain numeric — EMS canonical
    [InlineData("SAASA-42")]     // prefixed — older sync format
    [InlineData("saasa-42")]     // case-insensitive prefix
    public async Task Handle_EmsLegacyId_ExtractsNumericIdAndCallsApi(string legacyId)
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsCustomer(legacyId));
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = _projectId, Name = "P", EmsApiKey = "key" });
        _saasAClientMock
            .Setup(c => c.GetCompanySummaryAsync(It.IsAny<string?>(), 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsResponse(42));

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _saasAClientMock.Verify(
            c => c.GetCompanySummaryAsync(It.IsAny<string?>(), 42, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NonNumericLegacyIdAfterPrefix_ReturnsFailure()
    {
        // e.g. "SAASA-abc" — the part after prefix is not a number
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsCustomer("SAASA-abc"));

        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS kaynaklı değil");
    }

    // ── EMS API call ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_EmsApiThrows_ReturnsFailure()
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsCustomer("99"));
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = _projectId, Name = "P" });
        _saasAClientMock
            .Setup(c => c.GetCompanySummaryAsync(It.IsAny<string?>(), 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("EMS unreachable"));

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("EMS'ten");
    }

    // ── DTO mapping ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SuccessfulCall_MapsResponseToDto()
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsCustomer("42"));
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = _projectId, Name = "P", EmsApiKey = "test-key" });

        var emsResponse = new EmsCompanySummaryResponse(
            42,
            new EmsCompanySummaryTotals(CustomerCount: 10, ElevatorCount: 25, UserCount: 5),
            new List<EmsCompanyMonthlyStat>
            {
                new(Year: 2026, Month: 1, MaintenanceCount: 15, BreakdownCount: 3, ProposalCount: 2)
            });

        _saasAClientMock
            .Setup(c => c.GetCompanySummaryAsync("test-key", 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emsResponse);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.EmsCompanyId.Should().Be(42);
        dto.Totals.CustomerCount.Should().Be(10);
        dto.Totals.ElevatorCount.Should().Be(25);
        dto.Totals.UserCount.Should().Be(5);
        dto.Monthly.Should().HaveCount(1);
        dto.Monthly[0].Year.Should().Be(2026);
        dto.Monthly[0].Month.Should().Be(1);
        dto.Monthly[0].MaintenanceCount.Should().Be(15);
        dto.Monthly[0].BreakdownCount.Should().Be(3);
        dto.Monthly[0].ProposalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ProjectHasNullEmsApiKey_PassesNullToClient()
    {
        // Arrange — project has no EmsApiKey; SaasAClient will fall back to its DI-configured default
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsCustomer("7"));
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = _projectId, Name = "P", EmsApiKey = null });
        _saasAClientMock
            .Setup(c => c.GetCompanySummaryAsync(null, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmsResponse(7));

        // Act
        var result = await CreateHandler().Handle(new GetCustomerEmsSummaryQuery(_customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _saasAClientMock.Verify(
            c => c.GetCompanySummaryAsync(null, 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
