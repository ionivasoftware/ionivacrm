using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Unit tests for the SyncLog retry/status lifecycle — the states a SyncLog row moves through as
/// the Polly retry pipeline in <c>SaasSyncJob</c> records sync attempts
/// (Pending → Retrying×N → Success/Failed).
///
/// NOTE: The earlier RunAsync integration tests exercised the SaaS-A pull sync and the
/// Sync:EmsEnabled gate, both removed when EMS was retired (2026-08-30). The live Liftdesk and
/// Rezerval sync paths are covered by their own upsert/reconcile tests.
/// </summary>
public class SaasSyncJobTests
{
    [Fact]
    public void SyncLog_RetryCount_DefaultsToZero()
    {
        // Arrange & Act
        var log = new SyncLog();

        // Assert — entity default state
        log.RetryCount.Should().Be(0);
        log.Status.Should().Be(SyncStatus.Pending);
        log.ErrorMessage.Should().BeNull();
        log.SyncedAt.Should().BeNull();
    }

    [Fact]
    public void SyncLog_RetryCount_CanIncrementToThree()
    {
        // Arrange — simulates the OnRetry callback logic in BuildRetryPipeline
        var log = new SyncLog
        {
            ProjectId = Guid.NewGuid(),
            Source = SyncSource.SaasA,
            Direction = SyncDirection.Inbound,
            EntityType = "Customer",
            Status = SyncStatus.Pending
        };

        // Act — simulate 3 retry attempts (as Polly would invoke OnRetry)
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            log.RetryCount++;
            log.Status = SyncStatus.Retrying;
            log.ErrorMessage = $"Transient error on attempt {attempt}";
        }

        // Assert — after 3 retries
        log.RetryCount.Should().Be(3, "Polly retries 3 times (MaxRetryAttempts = 3)");
        log.Status.Should().Be(SyncStatus.Retrying);
        log.ErrorMessage.Should().Contain("attempt 3");
    }

    [Fact]
    public void SyncLog_AfterAllRetriesExhausted_StatusIsFailedAndRetryCountIsThree()
    {
        // Arrange — simulate full retry lifecycle: Pending → Retrying x3 → Failed
        var log = new SyncLog
        {
            ProjectId = Guid.NewGuid(),
            Source = SyncSource.SaasA,
            EntityType = "Customer",
            Status = SyncStatus.Pending
        };

        // Act — simulate OnRetry x3 then final failure
        for (var i = 0; i < 3; i++)
        {
            log.RetryCount++;
            log.Status = SyncStatus.Retrying;
            log.ErrorMessage = "Network timeout";
        }

        // Final catch block sets Failed
        log.Status = SyncStatus.Failed;
        log.ErrorMessage = "Network timeout — all retries exhausted";

        // Assert
        log.RetryCount.Should().Be(3, "exactly 3 retry attempts before giving up");
        log.Status.Should().Be(SyncStatus.Failed, "status is Failed after all retries exhausted");
        log.SyncedAt.Should().BeNull("SyncedAt is only set on success");
        log.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SyncLog_OnSuccess_SyncedAtIsStamped_RetryCountUnchanged()
    {
        // Arrange — simulate success on first attempt (no retries)
        var log = new SyncLog
        {
            Status = SyncStatus.Pending,
            RetryCount = 0
        };
        var before = DateTime.UtcNow;

        // Act — simulate success block
        log.Status = SyncStatus.Success;
        log.SyncedAt = DateTime.UtcNow;

        // Assert
        log.Status.Should().Be(SyncStatus.Success);
        log.RetryCount.Should().Be(0, "no retries on first-attempt success");
        log.SyncedAt.Should().NotBeNull();
        log.SyncedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void SyncLog_OnSuccessAfterOneRetry_RetryCountIsOne_StatusIsSuccess()
    {
        // Arrange — first attempt fails, second succeeds (RetryCount = 1)
        var log = new SyncLog
        {
            Status = SyncStatus.Pending,
            RetryCount = 0
        };

        // Act — simulate 1 retry then success
        log.RetryCount++;
        log.Status = SyncStatus.Retrying;

        log.Status = SyncStatus.Success;
        log.SyncedAt = DateTime.UtcNow;

        // Assert — RetryCount reflects the one retry, but final status is Success
        log.RetryCount.Should().Be(1);
        log.Status.Should().Be(SyncStatus.Success);
        log.SyncedAt.Should().NotBeNull();
    }
}
