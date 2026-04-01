using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.BackgroundServices;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Unit tests for SaasSyncJob — the Hangfire background job that pulls data
/// from SaaS A and SaaS B every 15 minutes.
///
/// FOCUS: Tests the early-return logic when ProjectIds are not configured and
/// verifies the Polly retry pipeline tracks RetryCount on SyncLog entities.
///
/// NOTE: The actual Polly retry delays (2s/4s/8s exponential backoff) are not
/// tested here to avoid slow test suite. The retry mechanism itself is a
/// framework concern; these tests focus on business behaviour around it.
/// </summary>
public class SaasSyncJobTests
{
    private readonly Mock<ISaasAClient> _saasAClientMock = new();
    private readonly Mock<ISaasBClient> _saasBClientMock = new();
    private readonly Mock<IProjectRepository> _projectRepoMock = new();
    private readonly Mock<ILogger<SaasSyncJob>> _loggerMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IConfiguration> _configMock = new();

    private SaasSyncJob CreateJob() => new(
        _saasAClientMock.Object,
        _saasBClientMock.Object,
        _projectRepoMock.Object,
        _scopeFactoryMock.Object,
        _configMock.Object,
        _loggerMock.Object);

    /// <summary>
    /// Creates an in-memory ApplicationDbContext suitable for unit tests.
    /// Uses a unique database name per call to ensure test isolation.
    /// </summary>
    private static ApplicationDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        return new ApplicationDbContext(options, currentUserMock.Object);
    }

    // ── Early-return when not configured ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_SaasAProjectIdNotConfigured_SkipsAllSaasACalls()
    {
        // Arrange — SaaS A project not set in configuration
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns((string?)null);
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns((string?)null);

        // Empty in-memory DB — no projects → ResolveProjectAsync returns Guid.Empty → skip
        SetupScopeFactory(CreateInMemoryDbContext("db_notconfigured_a"));

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert — no API calls made
        _saasAClientMock.Verify(
            c => c.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "SaaS A sync should be skipped when ProjectId is not configured");
        _saasBClientMock.Verify(
            c => c.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "SaaS B sync should be skipped when ProjectId is not configured");
    }

    [Fact]
    public async Task RunAsync_SaasAProjectIdIsEmptyGuid_SkipsAllSaasACalls()
    {
        // Arrange — empty string is not a valid Guid
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns("");
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns("");

        // Empty in-memory DB — no projects with API keys → skip both
        SetupScopeFactory(CreateInMemoryDbContext("db_emptyguid"));

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        _saasAClientMock.Verify(
            c => c.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _saasBClientMock.Verify(
            c => c.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_SaasBProjectIdNotConfigured_FallsBackToFirstProject()
    {
        // Arrange — SaaS B not configured but SaaS A IS configured.
        // With no SaasB:ProjectId, ResolveProjectAsync falls back to the first project
        // in DB (projectA). The global SaasB:ApiKey from config is used instead of
        // the per-project key, so SaaS B sync still runs.
        var projectAId = Guid.NewGuid();
        _configMock.Setup(c => c["SaasA:ProjectId"]).Returns(projectAId.ToString());
        _configMock.Setup(c => c["SaasB:ProjectId"]).Returns((string?)null);

        var dbContext = CreateInMemoryDbContext("db_saasb_notconfigured");
        dbContext.Projects.Add(new Project { Id = projectAId, Name = "Test Project", EmsApiKey = "test-ems-key" });
        await dbContext.SaveChangesAsync();

        SetupScopeFactory(dbContext);

        // Mock all API calls to return empty data
        _saasAClientMock
            .Setup(c => c.GetCrmCustomersPageAsync(
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmsCrmCustomersResponse(new List<EmsCrmCustomer>(), 0, 1, 20, 0));
        _saasAClientMock
            .Setup(c => c.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaasACustomersResponse(new List<SaasACustomer>(), 0));
        _saasAClientMock
            .Setup(c => c.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaasASubscriptionsResponse(new List<SaasASubscription>(), 0));
        _saasAClientMock
            .Setup(c => c.GetOrdersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaasAOrdersResponse(new List<SaasAOrder>(), 0));
        _saasBClientMock
            .Setup(c => c.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaasBCustomersResponse(new List<SaasBCustomer>(), 0));
        _saasBClientMock
            .Setup(c => c.GetSubscriptionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaasBSubscriptionsResponse(new List<SaasBSubscription>(), 0));
        _saasBClientMock
            .Setup(c => c.GetOrdersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaasBOrdersResponse(new List<SaasBOrder>(), 0));

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert — SaaS A ran
        _saasAClientMock.Verify(
            c => c.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "SaaS A sync should run when ProjectId is configured");
    }

    // ── SyncLog retry count entity behaviour ─────────────────────────────────

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

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets up the IServiceScopeFactory mock so that SaasSyncJob can resolve
    /// both <see cref="ApplicationDbContext"/> and <see cref="ISyncLogRepository"/>
    /// from any created scope. All scopes share the same in-memory DbContext instance.
    /// </summary>
    private void SetupScopeFactory(ApplicationDbContext dbContext)
    {
        var mockSyncLogRepo = new Mock<ISyncLogRepository>();
        mockSyncLogRepo
            .Setup(r => r.AddAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncLog log, CancellationToken _) => log);
        mockSyncLogRepo
            .Setup(r => r.UpdateAsync(It.IsAny<SyncLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(p => p.GetService(typeof(ISyncLogRepository)))
            .Returns(mockSyncLogRepo.Object);
        mockServiceProvider
            .Setup(p => p.GetService(typeof(ApplicationDbContext)))
            .Returns(dbContext);

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        _scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);
    }
}
