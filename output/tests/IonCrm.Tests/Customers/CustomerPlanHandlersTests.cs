using System.Net;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Commands.UpdateCustomerPlan;
using IonCrm.Application.Customers.Queries.GetCustomerPlan;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Unit tests for the Liftdesk subscription-plan handlers (get / change).
/// Covers: Liftdesk-only gating, missing project credentials, tenant isolation, input validation,
/// planId/tier pass-through and the 409 "no subscription yet" mapping.
/// </summary>
public class CustomerPlanHandlersTests
{
    private readonly Mock<ICustomerRepository>  _customerRepoMock = new();
    private readonly Mock<IProjectRepository>   _projectRepoMock  = new();
    private readonly Mock<ILiftdeskPlanClient>  _clientMock       = new();
    private readonly Mock<ICurrentUserService>  _userMock         = new();

    private static readonly Guid ProPlanId = Guid.Parse("3f8a0000-0000-4000-8000-000000000001");

    private GetCustomerPlanQueryHandler CreateGetHandler() => new(
        _customerRepoMock.Object, _projectRepoMock.Object, _clientMock.Object, _userMock.Object,
        Mock.Of<ILogger<GetCustomerPlanQueryHandler>>());

    private UpdateCustomerPlanCommandHandler CreateUpdateHandler() => new(
        _customerRepoMock.Object, _projectRepoMock.Object, _clientMock.Object, _userMock.Object,
        Mock.Of<ILogger<UpdateCustomerPlanCommandHandler>>());

    private Guid SetupLiftdeskCustomer(
        string legacyId = "LIFT-7",
        string? apiKey = "lift-key",
        string? baseUrl = "https://lift.example.com")
    {
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer
            {
                Id = customerId, ProjectId = projectId,
                CompanyName = "Liftdesk Firma", LegacyId = legacyId
            });
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, LiftdeskApiKey = apiKey, LiftdeskBaseUrl = baseUrl });

        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _userMock.Setup(u => u.UserId).Returns(Guid.NewGuid());

        return customerId;
    }

    private static LiftdeskCompanyPlan SamplePlan(string? warning = null) => new(
        CompanyId: 7,
        Current: new LiftdeskCurrentPlan(
            ProPlanId, "EMS Pro", "Pro", "Active", "Monthly",
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), AutoRenew: true),
        AvailablePlans:
        [
            new LiftdeskAvailablePlan(Guid.NewGuid(), "EMS Standart", "Standart", 500, 5000),
            new LiftdeskAvailablePlan(ProPlanId, "EMS Pro", "Pro", 900, 9000),
        ],
        Warning: warning);

    // ── GET ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_LiftdeskCustomer_ReturnsPlan()
    {
        var customerId = SetupLiftdeskCustomer();
        _clientMock
            .Setup(c => c.GetPlanAsync("https://lift.example.com", "lift-key", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SamplePlan(warning: "iyzico aboneliği elle güncellenmeli."));

        var result = await CreateGetHandler().Handle(new GetCustomerPlanQuery(customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Current!.Tier.Should().Be("Pro");
        result.Value.AvailablePlans.Should().HaveCount(2);
        result.Value.Warning.Should().Contain("iyzico");
    }

    [Fact]
    public async Task Get_CurrentNull_IsSuccess()
    {
        // Legacy tenant with no subscription row — the screen must still open.
        var customerId = SetupLiftdeskCustomer();
        _clientMock
            .Setup(c => c.GetPlanAsync(It.IsAny<string>(), It.IsAny<string>(), 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskCompanyPlan(7, null, [], null));

        var result = await CreateGetHandler().Handle(new GetCustomerPlanQuery(customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Current.Should().BeNull();
    }

    [Fact]
    public async Task Get_EmsCustomer_FailsAsNotLiftdesk()
    {
        var customerId = SetupLiftdeskCustomer(legacyId: "42"); // plain numeric = EMS

        var result = await CreateGetHandler().Handle(new GetCustomerPlanQuery(customerId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Liftdesk kaynaklı değil");
        _clientMock.Verify(c => c.GetPlanAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_OtherTenant_FailsWithoutCallingClient()
    {
        var customerId = SetupLiftdeskCustomer();
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() });

        var result = await CreateGetHandler().Handle(new GetCustomerPlanQuery(customerId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("yetki");
        _clientMock.Verify(c => c.GetPlanAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_MissingCredentials_Fails()
    {
        var customerId = SetupLiftdeskCustomer(apiKey: null);

        var result = await CreateGetHandler().Handle(new GetCustomerPlanQuery(customerId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("tanımlı değil");
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WithPlanId_PassesItThrough()
    {
        var customerId = SetupLiftdeskCustomer();
        LiftdeskPlanChangeRequest? sent = null;
        _clientMock
            .Setup(c => c.UpdatePlanAsync("https://lift.example.com", "lift-key", 7,
                It.IsAny<LiftdeskPlanChangeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, LiftdeskPlanChangeRequest, CancellationToken>(
                (_, _, _, body, _) => sent = body)
            .ReturnsAsync(SamplePlan());

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, null, ProPlanId, "Yearly"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sent!.PlanId.Should().Be(ProPlanId);
        sent.BillingPeriod.Should().Be("Yearly");
    }

    [Fact]
    public async Task Update_WithTier_PassesTrimmedTier()
    {
        var customerId = SetupLiftdeskCustomer();
        LiftdeskPlanChangeRequest? sent = null;
        _clientMock
            .Setup(c => c.UpdatePlanAsync(It.IsAny<string>(), It.IsAny<string>(), 7,
                It.IsAny<LiftdeskPlanChangeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, LiftdeskPlanChangeRequest, CancellationToken>(
                (_, _, _, body, _) => sent = body)
            .ReturnsAsync(SamplePlan());

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, "  Prime  ", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sent!.Tier.Should().Be("Prime");
        // Omitted period must stay null so Liftdesk keeps the current one.
        sent.BillingPeriod.Should().BeNull();
    }

    [Fact]
    public async Task Update_NeitherTierNorPlanId_FailsWithoutCallingClient()
    {
        var customerId = SetupLiftdeskCustomer();

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, null, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Paket seçilmedi");
        _clientMock.Verify(c => c.UpdatePlanAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<LiftdeskPlanChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_InvalidTier_Fails()
    {
        var customerId = SetupLiftdeskCustomer();

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, "Gold", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Geçersiz paket kademesi");
    }

    [Fact]
    public async Task Update_InvalidBillingPeriod_Fails()
    {
        var customerId = SetupLiftdeskCustomer();

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, "Pro", null, "Weekly"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Geçersiz ödeme dönemi");
    }

    [Fact]
    public async Task Update_Conflict_ExplainsMissingSubscription()
    {
        var customerId = SetupLiftdeskCustomer();
        _clientMock
            .Setup(c => c.UpdatePlanAsync(It.IsAny<string>(), It.IsAny<string>(), 7,
                It.IsAny<LiftdeskPlanChangeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HTTP 409", null, HttpStatusCode.Conflict));

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, "Prime", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Süre Uzat");
    }

    [Fact]
    public async Task Update_NonLiftdeskCustomer_Fails()
    {
        var customerId = SetupLiftdeskCustomer(legacyId: "REZV-5");

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, "Prime", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Liftdesk kaynaklı değil");
    }

    [Fact]
    public async Task Update_OtherTenant_FailsWithoutCallingClient()
    {
        var customerId = SetupLiftdeskCustomer();
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() });

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerPlanCommand(customerId, "Prime", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("yetki");
        _clientMock.Verify(c => c.UpdatePlanAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<LiftdeskPlanChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
