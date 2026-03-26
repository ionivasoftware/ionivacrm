using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Additional sync service tests covering:
/// - SyncLog lifecycle (created, updated to success/failed)
/// - Retry count tracking on SyncLog
/// - All incoming field mappings (email, phone, address, taxNumber)
/// - Inactive/Churned status mappings from SaaS A
/// - Null-safety for optional segment field
/// </summary>
public class SyncLogAndRetryTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ISyncLogRepository> _syncLogRepoMock = new();
    private readonly Mock<ILogger<ProcessSaasAWebhookCommandHandler>> _loggerMock = new();

    private ProcessSaasAWebhookCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _syncLogRepoMock.Object,
        _loggerMock.Object);

    private void SetupSyncLogRepo()
    {
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);
        _syncLogRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static string BuildPayload(
        string id, string name,
        string? email = "acme@example.com",
        string? phone = "555-0000",
        string? address = "1 Main St",
        string? taxNumber = null,
        string status = "active",
        string? segment = null)
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    // ── SyncLog lifecycle ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Success_SyncLogAddedThenUpdatedToSuccess()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        var addedLog = (SyncLog?)null;
        var updatedLog = (SyncLog?)null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => addedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);
        _syncLogRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => updatedLog = log)
            .Returns(Task.CompletedTask);

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "log-test-1", projectId,
            BuildPayload("log-test-1", "Log Corp"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — log was added first (Pending), then updated (Success)
        result.IsSuccess.Should().BeTrue();
        addedLog.Should().NotBeNull("SyncLog must be created at start");
        addedLog!.Status.Should().Be(SyncStatus.Success, "log is mutated to Success before save");
        addedLog.SyncedAt.Should().NotBeNull("SyncedAt must be stamped on success");
        addedLog.ProjectId.Should().Be(projectId);
        addedLog.Source.Should().Be(SyncSource.SaasA);
        addedLog.Direction.Should().Be(SyncDirection.Inbound);
        addedLog.EntityType.Should().Be("customer");
        addedLog.EntityId.Should().Be("log-test-1");

        _syncLogRepoMock.Verify(
            r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _syncLogRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Failure_SyncLogUpdatedToFailed_WithErrorMessage()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        SyncLog? capturedLog = null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => capturedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB timeout"));

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "fail-1", projectId,
            BuildPayload("fail-1", "Fail Corp"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        capturedLog!.Status.Should().Be(SyncStatus.Failed);
        capturedLog.ErrorMessage.Should().Contain("DB timeout");
        capturedLog.SyncedAt.Should().BeNull("SyncedAt is only stamped on success");
    }

    [Fact]
    public async Task Handle_SyncLog_PayloadStoredVerbatim()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        var rawPayload = BuildPayload("payload-id", "Payload Corp");
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
            "customer.created", "customer", "payload-id", projectId, rawPayload);

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — raw payload stored for debugging/retry
        capturedLog!.Payload.Should().Be(rawPayload);
    }

    // ── Field mapping completeness ────────────────────────────────────────────

    [Fact]
    public async Task Handle_NewCustomer_AllFieldsMappedCorrectly()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "full-map", projectId,
            BuildPayload(
                id: "full-map",
                name: "Full Map Corp",
                email: "full@map.com",
                phone: "555-7777",
                address: "99 Full St",
                taxNumber: "TAX123",
                status: "active",
                segment: "sme"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — every field is mapped from the external payload
        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.CompanyName.Should().Be("Full Map Corp");
        added.Email.Should().Be("full@map.com");
        added.Phone.Should().Be("555-7777");
        added.Address.Should().Be("99 Full St");
        added.TaxNumber.Should().Be("TAX123");
        added.Status.Should().Be(CustomerStatus.Active);
        added.Segment.Should().Be("sme");
        added.LegacyId.Should().Be("SAASA-full-map");
        added.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task Handle_UpdateExistingCustomer_AllFieldsUpdated()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        var existing = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = "SAASA-upd-001",
            CompanyName = "Old Name",
            Email = "old@example.com",
            Phone = "000-0000",
            Address = "Old Street",
            TaxNumber = null,
            Status = CustomerStatus.Lead,
            Segment = null
        };

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASA-upd-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _customerRepoMock
            .Setup(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new ProcessSaasAWebhookCommand(
            "customer.updated", "customer", "upd-001", projectId,
            BuildPayload(
                id: "upd-001",
                name: "New Name",
                email: "new@example.com",
                phone: "999-9999",
                address: "New Street",
                taxNumber: "VAT999",
                status: "active",
                segment: "enterprise"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — all mutable fields updated on the existing entity
        existing.CompanyName.Should().Be("New Name");
        existing.Email.Should().Be("new@example.com");
        existing.Phone.Should().Be("999-9999");
        existing.Address.Should().Be("New Street");
        existing.TaxNumber.Should().Be("VAT999");
        existing.Status.Should().Be(CustomerStatus.Active);
        existing.Segment.Should().Be("enterprise");
    }

    // ── Status mapping edge cases ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_StatusMapping_InactiveMapsToInactive()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "inactive-1", projectId,
            BuildPayload("inactive-1", "Inactive Corp", status: "inactive"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        added!.Status.Should().Be(CustomerStatus.Demo);
    }

    [Fact]
    public async Task Handle_StatusMapping_PassiveMapsToInactive()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "passive-1", projectId,
            BuildPayload("passive-1", "Passive Corp", status: "passive"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        added!.Status.Should().Be(CustomerStatus.Demo);
    }

    [Fact]
    public async Task Handle_StatusMapping_ChurnedMapsToChurned()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "churned-1", projectId,
            BuildPayload("churned-1", "Churned Corp", status: "churned"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        added!.Status.Should().Be(CustomerStatus.Churned);
    }

    // ── Segment mapping edge cases ────────────────────────────────────────────

    [Fact]
    public async Task Handle_SegmentMapping_SmeMapsCorrectly()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "sme-1", projectId,
            BuildPayload("sme-1", "SME Corp", segment: "sme"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        added!.Segment.Should().Be("sme");
    }

    [Fact]
    public async Task Handle_SegmentMapping_IndividualMapsCorrectly()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "ind-1", projectId,
            BuildPayload("ind-1", "Individual", segment: "individual"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        added!.Segment.Should().Be("individual");
    }

    [Fact]
    public async Task Handle_SegmentMapping_NullSegmentMapsToNull()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "noseg-1", projectId,
            BuildPayload("noseg-1", "No Segment Corp", segment: null));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — null segment treated gracefully
        added!.Segment.Should().BeNull();
    }

    // ── Upsert idempotency ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SameExternalIdTwice_DoesNotCreateDuplicate()
    {
        // Arrange — simulate processing the same event twice (replay / at-least-once delivery)
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        int addCalls = 0;
        int updateCalls = 0;
        var existing = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = "SAASA-idem-1",
            CompanyName = "Old Name"
        };

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASA-idem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((_, __) => addCalls++)
            .ReturnsAsync((Customer c, CancellationToken _) => c);
        _customerRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((_, __) => updateCalls++)
            .Returns(Task.CompletedTask);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "idem-1", projectId,
            BuildPayload("idem-1", "New Name"));

        // Act — process twice
        await CreateHandler().Handle(command, CancellationToken.None);
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — Add never called; Update called twice (once per event)
        addCalls.Should().Be(0, "duplicate should update, not insert");
        updateCalls.Should().Be(2, "each event triggers an update on the existing record");
    }

    // ── Non-customer entity type ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownEntityType_ReturnsSuccessWithoutCustomerAction()
    {
        // Arrange — entity type is "opportunity" which is not handled
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();

        var command = new ProcessSaasAWebhookCommand(
            "opportunity.created", "opportunity", "opp-1", projectId,
            "{\"id\":\"opp-1\",\"name\":\"Deal\"}");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — handler succeeds gracefully without touching customer repo
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(
            r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
