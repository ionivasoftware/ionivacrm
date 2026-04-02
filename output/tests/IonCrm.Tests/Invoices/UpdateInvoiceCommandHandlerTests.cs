using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Invoices.Commands.UpdateInvoice;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Invoices;

/// <summary>
/// Unit tests for <see cref="UpdateInvoiceCommandHandler"/>.
/// </summary>
public class UpdateInvoiceCommandHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<UpdateInvoiceCommandHandler>> _loggerMock = new();

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _invoiceId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    private UpdateInvoiceCommandHandler CreateHandler() => new(
        _invoiceRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private void SetupAuthorizedUser()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
    }

    private Invoice CreateDraftInvoice() => new()
    {
        Id = _invoiceId,
        ProjectId = _projectId,
        CustomerId = _customerId,
        Title = "Eski Başlık",
        Status = InvoiceStatus.Draft,
        LinesJson = "[]",
        NetTotal = 0m,
        GrossTotal = 0m
    };

    private static UpdateInvoiceCommand BuildCommand(string linesJson = "[]") => new()
    {
        InvoiceId = _invoiceId,
        Title = "Yeni Başlık",
        Description = "Güncellendi",
        InvoiceSeries = "B",
        InvoiceNumber = 7,
        IssueDate = new DateTime(2026, 3, 1),
        DueDate = new DateTime(2026, 3, 31),
        Currency = "EUR",
        LinesJson = linesJson
    };

    // ── Not found / authorization ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvoiceNotFound_ReturnsFailure()
    {
        // Arrange
        SetupAuthorizedUser();
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);

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
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDraftInvoice());

        // Act
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("erişim");
        _invoiceRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanUpdateInvoiceInAnyProject()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDraftInvoice());
        _invoiceRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ── Status guard ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InvoiceStatus.TransferredToParasut)]
    [InlineData(InvoiceStatus.Officialized)]
    public async Task Handle_NonDraftInvoice_ReturnsFailure(InvoiceStatus status)
    {
        // Arrange
        SetupAuthorizedUser();
        var invoice = CreateDraftInvoice();
        invoice.Status = status;
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("taslak");
        _invoiceRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Field updates ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DraftInvoice_UpdatesAllFields()
    {
        // Arrange
        SetupAuthorizedUser();
        var invoice = CreateDraftInvoice();
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Yeni Başlık");
        result.Value.Description.Should().Be("Güncellendi");
        result.Value.InvoiceSeries.Should().Be("B");
        result.Value.InvoiceNumber.Should().Be(7);
        result.Value.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Handle_DraftInvoice_RecomputesTotalsFromNewLines()
    {
        // Arrange: 2 × 100 TL, VAT 20%, no discount → net 200, gross 240
        SetupAuthorizedUser();
        var invoice = CreateDraftInvoice();
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        const string newLinesJson = """
            [{"description":"Ürün","quantity":2,"unitPrice":100,"vatRate":20,"discountValue":0,"discountType":"percent"}]
            """;

        // Act
        var result = await CreateHandler().Handle(BuildCommand(newLinesJson), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.NetTotal.Should().Be(200m);
        result.Value.GrossTotal.Should().Be(240m);
    }

    [Fact]
    public async Task Handle_DraftInvoice_DatesStoredAsUtc()
    {
        // Arrange
        SetupAuthorizedUser();
        var invoice = CreateDraftInvoice();
        Invoice? updatedInvoice = null;
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => updatedInvoice = inv)
            .Returns(Task.CompletedTask);

        // Act
        await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        updatedInvoice!.IssueDate.Kind.Should().Be(DateTimeKind.Utc);
        updatedInvoice.DueDate.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Handle_DraftInvoice_CallsUpdateAsyncOnce()
    {
        // Arrange
        SetupAuthorizedUser();
        _invoiceRepoMock
            .Setup(r => r.GetByIdAsync(_invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDraftInvoice());
        _invoiceRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await CreateHandler().Handle(BuildCommand(), CancellationToken.None);

        // Assert
        _invoiceRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
