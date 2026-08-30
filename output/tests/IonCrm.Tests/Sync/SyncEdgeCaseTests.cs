using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Tests.Sync;

/// <summary>
/// SaaS-B (RezervAl) inbound-webhook edge case tests covering:
/// - SaasB: Churned status maps to Churned (upper-case variant)
/// - SaasB: LegacyId prefix is always "SAASB-{customerId}"
/// - SaasB: unknown status defaults to Lead
/// - SaasB: unknown tier stored as free-text segment pass-through
/// - SaasB: SyncLog source is SaasB
///
/// (The SaaS-A inbound webhook was removed when EMS was retired 2026-08-30; its edge-case
/// tests were removed with it. SaaS-B/RezervAl remains the live inbound-webhook source.)
/// </summary>
public class SyncEdgeCaseTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ISyncLogRepository> _syncLogRepoMock = new();
    private readonly Mock<ILogger<ProcessSaasBWebhookCommandHandler>> _saasBLoggerMock = new();

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

    // ── SaaS B: tier passed through as free-text segment ─────────────────────────
    // Segment is a free-text field; any tier value from SaaS B is stored as-is.

    [Theory]
    [InlineData("PREMIUM")]
    [InlineData("STARTER")]
    [InlineData("gold")]
    public async Task SaasB_Tier_StoredAsSegmentPassThrough(string unknownTier)
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

        // Assert — Segment is free-text; tier value from SaaS B is stored as-is
        result.IsSuccess.Should().BeTrue();
        added!.Segment.Should().Be(unknownTier,
            $"tier '{unknownTier}' must be stored as segment as-is (free-text pass-through)");
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
}
