using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Invoices.Commands.CreateInvoice;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Invoices;

/// <summary>
/// Unit tests for <see cref="CreateInvoiceCommandHandler"/>.
/// </summary>
public class CreateInvoiceCommandHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<IParasutClient> _parasutClientMock = new();
    private readonly Mock<IParasutConnectionRepository> _connectionRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<CreateInvoiceCommandHandler>> _loggerMock = new();

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    private CreateInvoiceCommandHandler CreateHandler() => new(
        _invoiceRepoMock.Object,
        _customerRepoMock.Object,
        _parasutClientMock.Object,
        _connectionRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private void SetupAuthorizedUser()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
    }

    private Customer CreateTestCustomer(bool isEInvoicePayer = false, string? parasutContactId = null)
        => new()
        {
            Id = _customerId,
            ProjectId = _projectId,
            CompanyName = "Test Müşteri A.Ş.",
            IsEInvoicePayer = isEInvoicePayer,
            ParasutContactId = parasutContactId,
            EInvoiceAddress = isEInvoicePayer ? "PK:1234567890" : null
        };

    private static CreateInvoiceCommand BuildCommand(string linesJson = "[]") => new()
    {
        CustomerId = _customerId,
        Title = "Test Fatura",
        IssueDate = new DateTime(2026, 1, 1),
        DueDate = new DateTime(2026, 1, 31),
        Currency = "TRL",
        LinesJson = linesJson
    };

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
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("bulunamadı");
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // different project
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer());

        // Act
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("erişim");
        _invoiceRepoMock.Verify(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanCreateForAnyProject()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer());
        _invoiceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ── Core invoice creation ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_NoLines_CreatesDraftInvoiceWithZeroTotals()
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer());

        Invoice? savedInvoice = null;
        _invoiceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => savedInvoice = inv)
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);

        // Act
        var result = await CreateHandler().Handle(BuildCommand("[]"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        savedInvoice.Should().NotBeNull();
        savedInvoice!.Status.Should().Be(InvoiceStatus.Draft);
        savedInvoice.NetTotal.Should().Be(0m);
        savedInvoice.GrossTotal.Should().Be(0m);
        savedInvoice.ProjectId.Should().Be(_projectId);
        savedInvoice.CustomerId.Should().Be(_customerId);
    }

    [Fact]
    public async Task Handle_ValidCommand_WithLines_ComputesTotalsServerSide()
    {
        // Arrange: 2 × 100 TL, VAT 20%, no discount → net 200, gross 240
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer());

        Invoice? savedInvoice = null;
        _invoiceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => savedInvoice = inv)
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);

        const string linesJson = """
            [{"description":"Widget","quantity":2,"unitPrice":100,"vatRate":20,"discountValue":0,"discountType":"percent"}]
            """;

        // Act
        var result = await CreateHandler().Handle(BuildCommand(linesJson), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        savedInvoice!.NetTotal.Should().Be(200m);
        savedInvoice.GrossTotal.Should().Be(240m);
    }

    [Fact]
    public async Task Handle_WithDiscountedLines_ComputesDiscountAwareTotals()
    {
        // Arrange: 1 × 1000 TL, 10% discount → net 900; VAT 18% → gross 1062
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer());

        Invoice? savedInvoice = null;
        _invoiceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => savedInvoice = inv)
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);

        const string linesJson = """
            [{"description":"Hizmet","quantity":1,"unitPrice":1000,"vatRate":18,"discountValue":10,"discountType":"percent"}]
            """;

        // Act
        var result = await CreateHandler().Handle(BuildCommand(linesJson), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        savedInvoice!.NetTotal.Should().Be(900m);
        savedInvoice.GrossTotal.Should().Be(1062m);
    }

    [Fact]
    public async Task Handle_ValidCommand_IssueDateStoredAsUtc()
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer());

        Invoice? savedInvoice = null;
        _invoiceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => savedInvoice = inv)
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);

        // Act
        await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert — DateTimeKind must be Utc
        savedInvoice!.IssueDate.Kind.Should().Be(DateTimeKind.Utc);
        savedInvoice.DueDate.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ── Auto-transfer skipped when not e-invoice payer ────────────────────────

    [Fact]
    public async Task Handle_NotEInvoicePayer_DoesNotCallParasutClient()
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer(isEInvoicePayer: false));
        _invoiceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _parasutClientMock.Verify(
            c => c.CreateSalesInvoiceAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<IonCrm.Application.Common.Models.ExternalApis.CreateSalesInvoiceRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Returned DTO correctness ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_ReturnsDtoWithCorrectFields()
    {
        // Arrange
        SetupAuthorizedUser();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCustomer());
        _invoiceRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);

        var command = new CreateInvoiceCommand
        {
            CustomerId = _customerId,
            Title = "Nisan 2026 Fatura",
            Description = "Test açıklama",
            InvoiceSeries = "A",
            InvoiceNumber = 42,
            IssueDate = new DateTime(2026, 4, 1),
            DueDate = new DateTime(2026, 4, 30),
            Currency = "USD",
            LinesJson = "[]"
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Nisan 2026 Fatura");
        result.Value.Description.Should().Be("Test açıklama");
        result.Value.InvoiceSeries.Should().Be("A");
        result.Value.InvoiceNumber.Should().Be(42);
        result.Value.Currency.Should().Be("USD");
        result.Value.Status.Should().Be(InvoiceStatus.Draft);
        result.Value.ProjectId.Should().Be(_projectId);
        result.Value.CustomerId.Should().Be(_customerId);
    }
}
