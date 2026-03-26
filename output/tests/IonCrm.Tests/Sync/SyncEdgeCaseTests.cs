using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Sync service edge case tests covering:
/// - SaasA: completely invalid / null JSON payload → returns failure gracefully
/// - SaasA: unknown status string defaults to Lead
/// - SaasA: unknown segment string maps to null
/// - SaasA: LegacyId prefix is always "SAASA-{externalId}"
/// - SaasB: Churned status maps to Churned (upper-case variant)
/// - SaasB: LegacyId prefix is always "SAASB-{customerId}"
/// - SaasB: unknown status defaults to Lead
/// - SaasB: unknown tier defaults to null segment
/// - SaasA: UpdateExisting does NOT call AddAsync
/// - SaasA: NewCustomer does NOT call UpdateAsync
/// - SyncLog ID is always a non-empty Guid on creation
/// - SyncLog EntityType stored verbatim from command
/// - SyncLog ProjectId matches command ProjectId
/// </summary>
public class SyncEdgeCaseTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ISyncLogRepository> _syncLogRepoMock = new();
    private readonly Mock<ILogger<ProcessSaasAWebhookCommandHandler>> _saasALoggerMock = new();
    private readonly Mock<ILogger<ProcessSaasBWebhookCommandHandler>> _saasBLoggerMock = new();

    private ProcessSaasAWebhookCommandHandler CreateSaasAHandler() => new(
        _customerRepoMock.Object,
        _syncLogRepoMock.Object,
        _saasALoggerMock.Object);

    private ProcessSaasBWebhookCommandHandler CreateSaasBHandler() => new(
        _customerRepoMock.Object,
        _syncLogRepoMock.Object,
        _saasBLoggerMock.Object);

    private void SetupSyncLogRepo()
    {
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);
        _syncLogRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupNewCustomerRepo()
    {
        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);
    }

    private static string BuildSaasAPayload(
        string id = "ext-001",
        string name = "Test Corp",
        string status = "active",
        string? segment = null) =>
        JsonSerializer.Serialize(new
        {
            Id = id, Name = name, Email = (string?)null,
            Phone = (string?)null, Address = (string?)null, TaxNumber = (string?)null,
            Status = status, Segment = segment,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });

    private static string BuildSaasBPayload(
        string customerId = "B-ext-001",
        string fullName = "Beta Corp",
        string accountState = "ACTIVE",
        string? tier = null) =>
        JsonSerializer.Serialize(new
        {
            CustomerId = customerId,
            FullName = fullName,
            ContactEmail = (string?)null,
            Mobile = (string?)null,
            StreetAddress = (string?)null,
            TaxId = (string?)null,
            AccountState = accountState,
            Tier = tier,
            OwnerId = (string?)null,
            CreatedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

    // ── SaaS A: invalid JSON ──────────────────────────────────────────────────

    [Fact]
    public async Task SaasA_CompletelyInvalidJson_ReturnsFailed_SyncLogStatusSetToFailed()
    {
        // Arrange
        SetupSyncLogRepo();
        SyncLog? capturedLog = null;
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((l, _) => capturedLog = l)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "bad-1", Guid.NewGuid(),
            "{ this is not valid json !!!");

        // Act
        var result = await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert — handler must not crash; failure must be captured in sync log
        result.IsFailure.Should().BeTrue("invalid JSON must result in a failure response");
        capturedLog!.Status.Should().Be(SyncStatus.Failed, "sync log must record the failure");
        capturedLog.ErrorMessage.Should().NotBeNullOrEmpty("error message must be stored");
        capturedLog.SyncedAt.Should().BeNull("SyncedAt must only be set on success");
    }

    [Fact]
    public async Task SaasA_NullJsonPayload_ReturnsFailed()
    {
        // Arrange
        SetupSyncLogRepo();

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "null-1", Guid.NewGuid(),
            "null");

        // Act
        var result = await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue("'null' JSON payload must result in failure");
        result.FirstError.Should().Contain("deserialize",
            "failure message must explain the deserialisation problem");
    }

    // ── SaaS A: status fallback ───────────────────────────────────────────────

    [Theory]
    [InlineData("unknown")]
    [InlineData("DELETED")]
    [InlineData("pending")]
    [InlineData("")]
    public async Task SaasA_UnknownStatus_DefaultsToLead(string unknownStatus)
    {
        // Arrange
        SetupSyncLogRepo();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", $"unk-{unknownStatus}", Guid.NewGuid(),
            BuildSaasAPayload(id: $"unk-{unknownStatus}", status: unknownStatus));

        // Act
        var result = await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert — unknown status must default gracefully to Lead
        result.IsSuccess.Should().BeTrue("handler must not crash on unknown status");
        added!.Status.Should().Be(CustomerStatus.Lead,
            $"unknown status '{unknownStatus}' must default to Lead");
    }

    // ── SaaS A: segment fallback ──────────────────────────────────────────────

    [Theory]
    [InlineData("unknown_tier")]
    [InlineData("CORPORATE")]
    [InlineData("micro")]
    public async Task SaasA_UnknownSegment_DefaultsToNull(string unknownSegment)
    {
        // Arrange
        SetupSyncLogRepo();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", $"unseg-{unknownSegment}", Guid.NewGuid(),
            BuildSaasAPayload(id: $"unseg-{unknownSegment}", segment: unknownSegment));

        // Act
        var result = await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        added!.Segment.Should().BeNull(
            $"unknown segment '{unknownSegment}' must map to null, not throw");
    }

    // ── SaaS A: LegacyId format ───────────────────────────────────────────────

    [Fact]
    public async Task SaasA_NewCustomer_LegacyIdFormatIsSAASA_ExternalId()
    {
        // Arrange
        SetupSyncLogRepo();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASA-ext-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "ext-abc123", Guid.NewGuid(),
            BuildSaasAPayload(id: "ext-abc123"));

        // Act
        var result = await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert — LegacyId format must be "SAASA-{externalId}" exactly
        result.IsSuccess.Should().BeTrue();
        added!.LegacyId.Should().Be("SAASA-ext-abc123",
            "SaaS A LegacyId must use 'SAASA-' prefix to avoid collisions with SaaS B IDs");
    }

    [Fact]
    public async Task SaasA_ExistingCustomer_LookedUpByCorrectLegacyId()
    {
        // Arrange — verify GetByLegacyIdAsync is called with the right key
        SetupSyncLogRepo();
        string? capturedLegacyId = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((lid, _) => capturedLegacyId = lid)
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasAWebhookCommand(
            "customer.updated", "customer", "lookup-id", Guid.NewGuid(),
            BuildSaasAPayload(id: "lookup-id"));

        // Act
        await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert — exact LegacyId key used in lookup
        capturedLegacyId.Should().Be("SAASA-lookup-id",
            "GetByLegacyIdAsync must be called with 'SAASA-{entityId}'");
    }

    // ── SaaS A: update path never calls Add ──────────────────────────────────

    [Fact]
    public async Task SaasA_ExistingCustomer_AddAsyncNeverCalled()
    {
        // Arrange — existing customer exists for this LegacyId
        SetupSyncLogRepo();
        var existing = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
            LegacyId = "SAASA-upd-no-add",
            CompanyName = "Old Name"
        };

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASA-upd-no-add", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _customerRepoMock
            .Setup(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new ProcessSaasAWebhookCommand(
            "customer.updated", "customer", "upd-no-add", Guid.NewGuid(),
            BuildSaasAPayload(id: "upd-no-add", name: "New Name"));

        // Act
        await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert — update path must never create a duplicate
        _customerRepoMock.Verify(
            r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "updating existing customer must never call AddAsync");
        _customerRepoMock.Verify(
            r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()),
            Times.Once,
            "updating existing customer must call UpdateAsync exactly once");
    }

    // ── SaaS A: new customer path never calls Update ──────────────────────────

    [Fact]
    public async Task SaasA_NewCustomer_UpdateAsyncNeverCalled()
    {
        // Arrange
        SetupSyncLogRepo();
        SetupNewCustomerRepo();

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "new-no-upd", Guid.NewGuid(),
            BuildSaasAPayload(id: "new-no-upd"));

        // Act
        await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert — new customer path must never call UpdateAsync
        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "creating new customer must never call UpdateAsync");
        _customerRepoMock.Verify(
            r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "creating new customer must call AddAsync exactly once");
    }

    // ── SyncLog metadata correctness ─────────────────────────────────────────

    [Fact]
    public async Task SaasA_SyncLog_IdIsNonEmptyGuid()
    {
        // Arrange
        SetupSyncLogRepo();
        SetupNewCustomerRepo();
        SyncLog? capturedLog = null;
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((l, _) => capturedLog = l)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "log-id-test", Guid.NewGuid(),
            BuildSaasAPayload(id: "log-id-test"));

        // Act
        await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert — SyncLog must have a proper UUID
        capturedLog!.Id.Should().NotBe(Guid.Empty,
            "SyncLog must be assigned a non-empty Guid ID");
    }

    [Fact]
    public async Task SaasA_SyncLog_ProjectIdMatchesCommand()
    {
        // Arrange
        SetupSyncLogRepo();
        SetupNewCustomerRepo();
        var projectId = Guid.NewGuid();
        SyncLog? capturedLog = null;
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((l, _) => capturedLog = l)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "pid-test", projectId,
            BuildSaasAPayload(id: "pid-test"));

        // Act
        await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedLog!.ProjectId.Should().Be(projectId,
            "SyncLog.ProjectId must match the command's ProjectId for correct tenant attribution");
    }

    [Fact]
    public async Task SaasA_SyncLog_EntityTypeAndEntityIdStoredVerbatim()
    {
        // Arrange
        SetupSyncLogRepo();
        SetupNewCustomerRepo();
        SyncLog? capturedLog = null;
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((l, _) => capturedLog = l)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        var command = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "verbatim-entity-id", Guid.NewGuid(),
            BuildSaasAPayload(id: "verbatim-entity-id"));

        // Act
        await CreateSaasAHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedLog!.EntityType.Should().Be("customer");
        capturedLog.EntityId.Should().Be("verbatim-entity-id",
            "EntityId must be stored verbatim from the webhook command");
    }

    // ── SaaS B: Churned status (upper-case) ──────────────────────────────────

    [Fact]
    public async Task SaasB_ChurnedStatus_MapsToCchurned()
    {
        // Arrange
        SetupSyncLogRepo();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasBWebhookCommand(
            "customer.churned", "customer", "B-churn-1", Guid.NewGuid(),
            BuildSaasBPayload(customerId: "B-churn-1", accountState: "CHURNED"));

        // Act
        var result = await CreateSaasBHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        added!.Status.Should().Be(CustomerStatus.Churned,
            "CHURNED AccountState must map to CustomerStatus.Churned");
    }

    // ── SaaS B: LegacyId format ───────────────────────────────────────────────

    [Fact]
    public async Task SaasB_NewCustomer_LegacyIdFormatIsSAASB_CustomerId()
    {
        // Arrange
        SetupSyncLogRepo();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASB-B-xyz789", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-xyz789", Guid.NewGuid(),
            BuildSaasBPayload(customerId: "B-xyz789"));

        // Act
        var result = await CreateSaasBHandler().Handle(command, CancellationToken.None);

        // Assert — LegacyId must use "SAASB-" prefix
        result.IsSuccess.Should().BeTrue();
        added!.LegacyId.Should().Be("SAASB-B-xyz789",
            "SaaS B LegacyId must use 'SAASB-' prefix to avoid collisions with SaaS A IDs");
    }

    // ── SaaS B: unknown status defaults to Lead ───────────────────────────────

    [Theory]
    [InlineData("PENDING")]
    [InlineData("SUSPENDED")]
    [InlineData("")]
    public async Task SaasB_UnknownAccountState_DefaultsToLead(string unknownState)
    {
        // Arrange
        SetupSyncLogRepo();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", $"B-unk-{unknownState}", Guid.NewGuid(),
            BuildSaasBPayload(customerId: $"B-unk-{unknownState}", accountState: unknownState));

        // Act
        var result = await CreateSaasBHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("handler must not crash on unknown AccountState");
        added!.Status.Should().Be(CustomerStatus.Lead,
            $"unknown AccountState '{unknownState}' must default to Lead");
    }

    // ── SaaS B: unknown tier defaults to null segment ─────────────────────────

    [Theory]
    [InlineData("PREMIUM")]
    [InlineData("STARTER")]
    [InlineData("gold")]
    public async Task SaasB_UnknownTier_DefaultsToNullSegment(string unknownTier)
    {
        // Arrange
        SetupSyncLogRepo();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", $"B-tier-{unknownTier}", Guid.NewGuid(),
            BuildSaasBPayload(customerId: $"B-tier-{unknownTier}", tier: unknownTier));

        // Act
        var result = await CreateSaasBHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        added!.Segment.Should().BeNull(
            $"unknown Tier '{unknownTier}' must map to null segment");
    }

    // ── SaaS B: SyncLog source is SaasB ──────────────────────────────────────

    [Fact]
    public async Task SaasB_SyncLog_SourceIsSaasB()
    {
        // Arrange
        SetupSyncLogRepo();
        SetupNewCustomerRepo();
        SyncLog? capturedLog = null;
        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((l, _) => capturedLog = l)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-src-check", Guid.NewGuid(),
            BuildSaasBPayload(customerId: "B-src-check"));

        // Act
        await CreateSaasBHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedLog!.Source.Should().Be(SyncSource.SaasB,
            "SaaS B webhooks must record SaasB as the sync source");
        capturedLog.Direction.Should().Be(SyncDirection.Inbound,
            "webhooks are inbound sync events");
    }

    // ── Cross-source LegacyId isolation ──────────────────────────────────────

    [Fact]
    public async Task SaasA_And_SaasB_WithSameExternalId_CreateSeparateRecords()
    {
        // This test verifies that "SAASA-100" and "SAASB-100" are treated as different
        // customers even though the numeric portion is the same.
        // Arrange
        SetupSyncLogRepo();
        int addCalls = 0;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASA-100", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASB-100", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((_, __) => addCalls++)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var saasACommand = new ProcessSaasAWebhookCommand(
            "customer.created", "customer", "100", Guid.NewGuid(),
            BuildSaasAPayload(id: "100", name: "Shared ID Corp from SaaS A"));

        var saasBCommand = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "100", Guid.NewGuid(),
            BuildSaasBPayload(customerId: "100", fullName: "Shared ID Corp from SaaS B"));

        // Act
        await CreateSaasAHandler().Handle(saasACommand, CancellationToken.None);
        await CreateSaasBHandler().Handle(saasBCommand, CancellationToken.None);

        // Assert — two separate customer records must be created (one per source)
        addCalls.Should().Be(2,
            "the same numeric external ID from different sources must create SEPARATE records " +
            "because their LegacyId prefixes differ (SAASA-100 vs SAASB-100)");
    }
}
