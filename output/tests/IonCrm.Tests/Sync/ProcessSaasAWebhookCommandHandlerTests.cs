using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Tests.Sync;

public class ProcessSaasAWebhookCommandHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ISyncLogRepository> _syncLogRepoMock = new();
    private readonly Mock<ILogger<ProcessSaasAWebhookCommandHandler>> _loggerMock = new();

    private ProcessSaasAWebhookCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _syncLogRepoMock.Object,
        _loggerMock.Object);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static string BuildCustomerPayload(
        string id = "saas-123",
        string name = "Acme Corp",
        string? email = "acme@example.com",
        string? phone = "555-1234",
        string? address = "123 Main St",
        string? taxNumber = null,
        string status = "active",
        string? segment = "enterprise",
        string? assignedUserId = null)
    {
        return JsonSerializer.Serialize(new
        {
            Id = id,
            Name = name,
            Email = email,
            Phone = phone,
            Address = address,
            TaxNumber = taxNumber,
            Status = status,
            Segment = segment,
            AssignedUserId = assignedUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private void SetupSyncLogAdd()
    {
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);
        _syncLogRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_ValidCustomerPayload_CreatesNewCustomer()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASA-saas-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "saas-123", projectId,
            BuildCustomerPayload("saas-123", "Acme Corp"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(
            r => r.AddAsync(It.Is<Customer>(c =>
                c.LegacyId == "SAASA-saas-123" &&
                c.CompanyName == "Acme Corp" &&
                c.ProjectId == projectId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingCustomer_UpdatesExistingRecord()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        var existing = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = "SAASA-saas-456",
            CompanyName = "Old Name",
            Status = CustomerStatus.Lead
        };

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASA-saas-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _customerRepoMock
            .Setup(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new ProcessSaasAWebhookCommand(
            "customer.updated", "customer", "saas-456", projectId,
            BuildCustomerPayload("saas-456", "New Name", status: "active"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existing.CompanyName.Should().Be("New Name");
        existing.Status.Should().Be(CustomerStatus.Active);
        _customerRepoMock.Verify(
            r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()),
            Times.Once);
        _customerRepoMock.Verify(
            r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidPayload_CreatesSyncLogWithPendingThenSuccess()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        SyncLog? capturedLog = null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => capturedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "saas-789", projectId,
            BuildCustomerPayload("saas-789"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedLog.Should().NotBeNull();
        capturedLog!.ProjectId.Should().Be(projectId);
        capturedLog.Source.Should().Be(SyncSource.SaasA);
        capturedLog.Direction.Should().Be(SyncDirection.Inbound);
        // After successful processing, the log should be updated to Success
        capturedLog.Status.Should().Be(SyncStatus.Success);
        capturedLog.SyncedAt.Should().NotBeNull();
        _syncLogRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidJsonPayload_ReturnsFailed()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();

        // "null" JSON literal deserializes to null for a record type
        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "saas-000", projectId,
            "null");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("deserialize");
    }

    [Fact]
    public async Task Handle_ExceptionDuringProcessing_SetsLogStatusToFailed()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        SyncLog? capturedLog = null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => capturedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "saas-err", projectId,
            BuildCustomerPayload("saas-err"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Database error");
        capturedLog.Should().NotBeNull();
        capturedLog!.Status.Should().Be(SyncStatus.Failed);
        capturedLog.ErrorMessage.Should().Contain("Database error");
        _syncLogRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NewCustomer_LegacyIdFormattedCorrectly()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        Customer? addedCustomer = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => addedCustomer = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "my-external-id", projectId,
            BuildCustomerPayload("my-external-id"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        addedCustomer.Should().NotBeNull();
        addedCustomer!.LegacyId.Should().Be("SAASA-my-external-id");
    }

    [Fact]
    public async Task Handle_StatusMapping_ActiveMapsCorrectly()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        Customer? addedCustomer = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => addedCustomer = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "active-cust", projectId,
            BuildCustomerPayload("active-cust", status: "active"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        addedCustomer!.Status.Should().Be(CustomerStatus.Active);
    }

    [Fact]
    public async Task Handle_StatusMapping_LeadMapsCorrectly()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        Customer? addedCustomer = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => addedCustomer = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "lead-cust", projectId,
            BuildCustomerPayload("lead-cust", status: "lead"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        addedCustomer!.Status.Should().Be(CustomerStatus.Lead);
    }

    [Fact]
    public async Task Handle_StatusMapping_UnknownStatusMapsToLead()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        Customer? addedCustomer = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => addedCustomer = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "unknown-cust", projectId,
            BuildCustomerPayload("unknown-cust", status: "weird_unknown_status"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        addedCustomer!.Status.Should().Be(CustomerStatus.Lead);
    }

    [Fact]
    public async Task Handle_SegmentMapping_EnterpriseMapsCorrectly()
    {
        // Arrange
        SetupSyncLogAdd();
        var projectId = Guid.NewGuid();
        Customer? addedCustomer = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => addedCustomer = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "enterprise-cust", projectId,
            BuildCustomerPayload("enterprise-cust", segment: "enterprise"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        addedCustomer!.Segment.Should().Be("enterprise");
    }
}
