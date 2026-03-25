using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Sync.Commands.NotifySaas;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Tests for NotifySaasCommandHandler — the OUTBOUND sync / callback handler.
/// Verifies that CRM events are forwarded to SaaS A and SaaS B correctly,
/// that SyncLogs record Direction = Outbound, and that partial failures
/// (one SaaS down) are handled gracefully.
/// </summary>
public class NotifySaasCommandHandlerTests
{
    private readonly Mock<ISaasAClient> _saasAClientMock = new();
    private readonly Mock<ISaasBClient> _saasBClientMock = new();
    private readonly Mock<ISyncLogRepository> _syncLogRepoMock = new();
    private readonly Mock<ILogger<NotifySaasCommandHandler>> _loggerMock = new();

    private NotifySaasCommandHandler CreateHandler() => new(
        _saasAClientMock.Object,
        _saasBClientMock.Object,
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

    private static NotifySaasCommand BuildCommand(
        Guid? projectId = null,
        bool notifyA = true,
        bool notifyB = true,
        string eventType = "status_changed",
        string entityType = "customer",
        string entityId = "cust-123",
        string payload = "{}")
        => new(
            EventType: eventType,
            EntityType: entityType,
            EntityId: entityId,
            ProjectId: projectId ?? Guid.NewGuid(),
            PayloadJson: payload,
            NotifySaasA: notifyA,
            NotifySaasB: notifyB);

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_BothNotificationsSucceed_ReturnsSuccess()
    {
        // Arrange
        SetupSyncLogRepo();

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _saasBClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(notifyA: true, notifyB: true);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _saasAClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _saasBClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OnlyNotifySaasA_OnlySaasAClientCalled()
    {
        // Arrange
        SetupSyncLogRepo();

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(notifyA: true, notifyB: false);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _saasAClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _saasBClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_OnlyNotifySaasB_OnlySaasBClientCalled()
    {
        // Arrange
        SetupSyncLogRepo();

        _saasBClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(notifyA: false, notifyB: true);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _saasAClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _saasBClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NeitherNotified_NoClientCallsAndSucceeds()
    {
        // Arrange
        SetupSyncLogRepo();

        var command = BuildCommand(notifyA: false, notifyB: false);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — no errors when nothing is notified
        result.IsSuccess.Should().BeTrue();
        _saasAClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _saasBClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Failure cases ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SaasAClientThrows_ReturnsFailureWithErrorMessage()
    {
        // Arrange
        SetupSyncLogRepo();

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SaaS A unreachable"));

        var command = BuildCommand(notifyA: true, notifyB: false);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("SaaS A unreachable");
    }

    [Fact]
    public async Task Handle_SaasBClientThrows_ReturnsFailureWithErrorMessage()
    {
        // Arrange
        SetupSyncLogRepo();

        _saasBClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SaaS B unreachable"));

        var command = BuildCommand(notifyA: false, notifyB: true);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("SaaS B unreachable");
    }

    [Fact]
    public async Task Handle_BothClientsFail_ReturnsFailureWithBothErrors()
    {
        // Arrange
        SetupSyncLogRepo();

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SaaS A down"));
        _saasBClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SaaS B down"));

        var command = BuildCommand(notifyA: true, notifyB: true);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — both errors reported
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().HaveCount(2, "one error per failed notification");
        result.Errors.Should().Contain(e => e.Contains("SaaS A down"));
        result.Errors.Should().Contain(e => e.Contains("SaaS B down"));
    }

    [Fact]
    public async Task Handle_SaasAFails_SaasB_StillAttempted()
    {
        // Arrange — even when SaaS A fails, SaaS B should still be called
        SetupSyncLogRepo();

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SaaS A error"));
        _saasBClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(notifyA: true, notifyB: true);

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — SaaS B was still called despite SaaS A failure
        _saasBClientMock.Verify(
            c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── SyncLog outbound direction ─────────────────────────────────────────

    [Fact]
    public async Task Handle_SaasANotification_SyncLogHasOutboundDirection()
    {
        // Arrange
        SetupSyncLogRepo();
        var projectId = Guid.NewGuid();
        SyncLog? capturedLog = null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => capturedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(projectId: projectId, notifyA: true, notifyB: false,
            eventType: "subscription_extended", entityType: "customer", entityId: "C-777");

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — outbound sync log metadata
        capturedLog.Should().NotBeNull();
        capturedLog!.Direction.Should().Be(SyncDirection.Outbound);
        capturedLog.Source.Should().Be(SyncSource.SaasA);
        capturedLog.ProjectId.Should().Be(projectId);
        capturedLog.EntityType.Should().Be("customer");
        capturedLog.EntityId.Should().Be("C-777");
        capturedLog.Status.Should().Be(SyncStatus.Success);
    }

    [Fact]
    public async Task Handle_SaasBNotification_SyncLogHasSaasBSource()
    {
        // Arrange
        SetupSyncLogRepo();
        SyncLog? capturedLog = null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => capturedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _saasBClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(notifyA: false, notifyB: true);

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedLog!.Source.Should().Be(SyncSource.SaasB);
        capturedLog.Direction.Should().Be(SyncDirection.Outbound);
    }

    [Fact]
    public async Task Handle_SaasAFails_SyncLogStatusSetToFailed()
    {
        // Arrange
        SetupSyncLogRepo();
        SyncLog? capturedLog = null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => capturedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("timeout"));

        var command = BuildCommand(notifyA: true, notifyB: false);

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — failed notification logs its error
        capturedLog!.Status.Should().Be(SyncStatus.Failed);
        capturedLog.ErrorMessage.Should().Contain("timeout");
        capturedLog.SyncedAt.Should().BeNull("SyncedAt only stamped on success");
    }

    [Fact]
    public async Task Handle_BothNotificationsSucceed_TwoSyncLogsCreated()
    {
        // Arrange
        SetupSyncLogRepo();
        var logCount = 0;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((_, __) => logCount++)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _saasBClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasBCallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(notifyA: true, notifyB: true);

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — one SyncLog per outbound notification
        logCount.Should().Be(2, "each SaaS notification gets its own SyncLog");
    }

    // ── Payload forwarding ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SaasANotification_SyncLogPayloadMatchesCommand()
    {
        // Arrange
        SetupSyncLogRepo();
        const string expectedPayload = "{\"newStatus\":\"Active\"}";
        SyncLog? capturedLog = null;

        _syncLogRepoMock
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<SyncLog, CancellationToken>((log, _) => capturedLog = log)
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);

        _saasAClientMock
            .Setup(c => c.NotifyCallbackAsync(It.IsAny<SaasACallbackPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = BuildCommand(notifyA: true, notifyB: false, payload: expectedPayload);

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — raw payload stored verbatim in SyncLog for auditability
        capturedLog!.Payload.Should().Be(expectedPayload);
    }
}
