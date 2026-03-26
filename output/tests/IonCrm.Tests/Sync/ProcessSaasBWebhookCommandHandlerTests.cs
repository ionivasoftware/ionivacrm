using IonCrm.Application.Features.Sync.Commands.ProcessWebhook;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Tests for ProcessSaasBWebhookCommandHandler — the SaaS B inbound sync handler.
/// SaaS B uses different field names: CustomerId, FullName, ContactEmail, Mobile,
/// StreetAddress, TaxId, AccountState (UPPER), Tier (UPPER).
/// </summary>
public class ProcessSaasBWebhookCommandHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ISyncLogRepository> _syncLogRepoMock = new();
    private readonly Mock<ILogger<ProcessSaasBWebhookCommandHandler>> _loggerMock = new();

    private ProcessSaasBWebhookCommandHandler CreateHandler() => new(
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

    /// <summary>Builds a SaaS B JSON customer payload using their field names.</summary>
    private static string BuildSaasBPayload(
        string customerId = "B-001",
        string fullName = "Beta Corp",
        string? contactEmail = "beta@example.com",
        string? mobile = "555-9999",
        string? streetAddress = "99 Beta Ave",
        string? taxId = null,
        string accountState = "ACTIVE",
        string? tier = "ENTERPRISE",
        long? createdTs = null,
        long? updatedTs = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return JsonSerializer.Serialize(new
        {
            CustomerId = customerId,
            FullName = fullName,
            ContactEmail = contactEmail,
            Mobile = mobile,
            StreetAddress = streetAddress,
            TaxId = taxId,
            AccountState = accountState,
            Tier = tier,
            OwnerId = (string?)null,
            CreatedTimestamp = createdTs ?? now,
            UpdatedTimestamp = updatedTs ?? now
        });
    }

    // ── New customer creation ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NewCustomer_CreatesWithSaasBLegacyId()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        Customer? added = null;

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASB-B-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-001", projectId,
            BuildSaasBPayload("B-001", "Beta Corp"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.LegacyId.Should().Be("SAASB-B-001");
        added.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task Handle_NewCustomer_AllFieldsMappedFromSaasBNames()
    {
        // Arrange — verify SaaS B field name mapping (FullName, ContactEmail, Mobile, etc.)
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

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-full", projectId,
            BuildSaasBPayload(
                customerId: "B-full",
                fullName: "Full Beta Corp",
                contactEmail: "full@beta.com",
                mobile: "555-8888",
                streetAddress: "42 Beta Street",
                taxId: "BETA-TAX",
                accountState: "ACTIVE",
                tier: "ENTERPRISE"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — all SaaS B fields correctly mapped to CRM fields
        result.IsSuccess.Should().BeTrue();
        added!.CompanyName.Should().Be("Full Beta Corp");       // FullName → CompanyName
        added.Email.Should().Be("full@beta.com");               // ContactEmail → Email
        added.Phone.Should().Be("555-8888");                    // Mobile → Phone
        added.Address.Should().Be("42 Beta Street");            // StreetAddress → Address
        added.TaxNumber.Should().Be("BETA-TAX");                // TaxId → TaxNumber
        added.Status.Should().Be(CustomerStatus.Active);
        added.Segment.Should().Be("Enterprise");
    }

    // ── Upsert existing customer ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_ExistingCustomer_UpdatesAllFields()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        var existing = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = "SAASB-B-existing",
            CompanyName = "Old Name",
            Email = "old@beta.com",
            Phone = "000-0000",
            Status = CustomerStatus.Lead,
            Segment = null
        };

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASB-B-existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _customerRepoMock
            .Setup(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new ProcessSaasBWebhookCommand(
            "customer.updated", "customer", "B-existing", projectId,
            BuildSaasBPayload(
                customerId: "B-existing",
                fullName: "New Name",
                contactEmail: "new@beta.com",
                mobile: "111-1111",
                streetAddress: "New Street",
                taxId: "NEW-TAX",
                accountState: "ACTIVE",
                tier: "SME"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — all mutable fields updated
        result.IsSuccess.Should().BeTrue();
        existing.CompanyName.Should().Be("New Name");
        existing.Email.Should().Be("new@beta.com");
        existing.Phone.Should().Be("111-1111");
        existing.Address.Should().Be("New Street");
        existing.TaxNumber.Should().Be("NEW-TAX");
        existing.Status.Should().Be(CustomerStatus.Active);
        existing.Segment.Should().Be("SME");

        _customerRepoMock.Verify(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
        _customerRepoMock.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SameCustomerTwice_DoesNotCreateDuplicate()
    {
        // Arrange — simulate at-least-once webhook delivery
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        int addCalls = 0;
        int updateCalls = 0;
        var existing = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectId, LegacyId = "SAASB-B-dup",
            CompanyName = "Dup Corp"
        };

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync("SAASB-B-dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((_, __) => addCalls++)
            .ReturnsAsync((Customer c, CancellationToken _) => c);
        _customerRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((_, __) => updateCalls++)
            .Returns(Task.CompletedTask);

        var command = new ProcessSaasBWebhookCommand(
            "customer.updated", "customer", "B-dup", projectId,
            BuildSaasBPayload("B-dup", "Dup Corp"));

        // Act — process twice
        await CreateHandler().Handle(command, CancellationToken.None);
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — no new records; updates existing both times
        addCalls.Should().Be(0, "duplicate must not create new records");
        updateCalls.Should().Be(2, "each event triggers an update");
    }

    // ── SyncLog behavior ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Success_SyncLogHasSaasBSourceAndInboundDirection()
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
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-log-1", projectId,
            BuildSaasBPayload("B-log-1", "Log Corp"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedLog.Should().NotBeNull();
        capturedLog!.Source.Should().Be(SyncSource.SaasB, "SaaS B webhooks must use SaasB source");
        capturedLog.Direction.Should().Be(SyncDirection.Inbound);
        capturedLog.ProjectId.Should().Be(projectId);
        capturedLog.EntityType.Should().Be("customer");
        capturedLog.EntityId.Should().Be("B-log-1");
        capturedLog.Status.Should().Be(SyncStatus.Success);
        capturedLog.SyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_Exception_SyncLogStatusSetToFailed()
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
            .ThrowsAsync(new Exception("SaaS B DB failure"));

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-err", projectId,
            BuildSaasBPayload("B-err", "Error Corp"));

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        capturedLog!.Status.Should().Be(SyncStatus.Failed);
        capturedLog.ErrorMessage.Should().Contain("SaaS B DB failure");
        capturedLog.SyncedAt.Should().BeNull("SyncedAt only stamped on success");
    }

    [Fact]
    public async Task Handle_InvalidJson_ReturnsFailed()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-null", projectId,
            "null");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("deserialize");
    }

    [Fact]
    public async Task Handle_SyncLogCreatedOnce_ThenUpdatedOnce()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();

        _customerRepoMock
            .Setup(r => r.GetByLegacyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-once", projectId,
            BuildSaasBPayload("B-once", "Once Corp"));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — exactly one Add and one Update to SyncLog
        _syncLogRepoMock.Verify(
            r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()), Times.Once);
        _syncLogRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Status mapping (SaaS B uses UPPER-CASE) ───────────────────────────────

    [Theory]
    [InlineData("ACTIVE", CustomerStatus.Active)]
    [InlineData("active", CustomerStatus.Active)]   // case-insensitive mapping check
    [InlineData("LEAD", CustomerStatus.Lead)]
    [InlineData("INACTIVE", CustomerStatus.Demo)]
    [InlineData("PASSIVE", CustomerStatus.Demo)]
    [InlineData("CHURNED", CustomerStatus.Churned)]
    [InlineData("UNKNOWN_STATE", CustomerStatus.Lead)]  // unknown defaults to Lead
    public async Task Handle_StatusMapping_MapsCorrectly(string accountState, CustomerStatus expected)
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

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", $"B-status-{accountState}", projectId,
            BuildSaasBPayload($"B-status-{accountState}", "Status Corp", accountState: accountState));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        added!.Status.Should().Be(expected,
            $"AccountState '{accountState}' should map to {expected}");
    }

    // ── Tier/Segment mapping ──────────────────────────────────────────────────

    [Theory]
    [InlineData("ENTERPRISE")]
    [InlineData("SME")]
    [InlineData("Tekil Restoran")]
    public async Task Handle_TierMapping_PassesThroughAsString(string tier)
    {
        // Segment is now a free string passed through as-is from SaaS
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

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", $"B-tier-{tier}", projectId,
            BuildSaasBPayload($"B-tier-{tier}", "Tier Corp", tier: tier));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        added!.Segment.Should().Be(tier, $"Tier '{tier}' should be passed through as-is");
    }

    [Fact]
    public async Task Handle_NullTier_SegmentIsNull()
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

        var command = new ProcessSaasBWebhookCommand(
            "customer.created", "customer", "B-notier", projectId,
            BuildSaasBPayload("B-notier", "No Tier Corp", tier: null));

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — null tier maps to null segment
        added!.Segment.Should().BeNull();
    }

    // ── Non-customer entity type ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_SubscriptionType_NoCustomerAction()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();

        var command = new ProcessSaasBWebhookCommand(
            "subscription.created", "subscription", "SUB-1", projectId,
            "{\"SubId\":\"SUB-1\",\"ClientId\":\"CLI-1\"}");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — succeeds but never touches customer repo
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(
            r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
