using FluentAssertions;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Commands.CreateEmsImpersonation;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace IonCrm.Tests.SupportAccess;

public class ImpersonationConsentTests
{
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<ISaasAClient> _saas = new();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly Mock<ISupportAccessService> _consent = new();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Customer _customer;

    public ImpersonationConsentTests()
    {
        _customer = new Customer { Id = Guid.NewGuid(), ProjectId = _projectId, CompanyName = "Apex", LegacyId = "LIFT-42" };
        _customers.Setup(r => r.GetByIdAsync(_customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_customer);
        _projects.Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync(new Project { Id = _projectId, Name = "P", LiftdeskApiKey = "k", LiftdeskBaseUrl = "https://liftdesk.test" });
        _user.Setup(u => u.IsSuperAdmin).Returns(true); _user.Setup(u => u.ProjectIds).Returns(new List<Guid>()); _user.Setup(u => u.Email).Returns("op@ioniva.com");
        _saas.Setup(s => s.CreateImpersonationAsync(It.IsAny<string?>(), 42, "u-1", "op@ioniva.com", It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new EmsImpersonationResponse("https://liftdesk.test/login?code=secret", DateTime.UtcNow.AddMinutes(2)));
    }

    private CreateEmsImpersonationCommandHandler Handler() => new(_customers.Object, _projects.Object, _saas.Object, _user.Object, _consent.Object, Mock.Of<ILogger<CreateEmsImpersonationCommandHandler>>());

    [Fact]
    public async Task Login_under_an_unusable_consent_is_refused_before_liftdesk_is_called()
    {
        var consentId = Guid.NewGuid();
        _consent.Setup(c => c.ValidateForLoginAsync(consentId, _customer.Id, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync("Müşteri izin isteğini henüz yanıtlamadı.");
        var r = await Handler().Handle(new CreateEmsImpersonationCommand(_customer.Id, "u-1", "Bakım listesi boş görünüyor", consentId), CancellationToken.None);
        r.IsSuccess.Should().BeFalse(); r.FirstError.Should().Be("Müşteri izin isteğini henüz yanıtlamadı.");
        _saas.Verify(s => s.CreateImpersonationAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
        _consent.Verify(c => c.MarkUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_under_a_valid_consent_is_issued_and_counted()
    {
        var consentId = Guid.NewGuid();
        _consent.Setup(c => c.ValidateForLoginAsync(consentId, _customer.Id, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var r = await Handler().Handle(new CreateEmsImpersonationCommand(_customer.Id, "u-1", "Bakım listesi boş görünüyor", consentId), CancellationToken.None);
        r.IsSuccess.Should().BeTrue(r.FirstError); r.Value!.LoginUrl.Should().Contain("code=secret");
        _consent.Verify(c => c.MarkUsedAsync(consentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_without_consent_still_works_as_before()
    {
        var r = await Handler().Handle(new CreateEmsImpersonationCommand(_customer.Id, "u-1", "Bakım listesi boş görünüyor"), CancellationToken.None);
        r.IsSuccess.Should().BeTrue(r.FirstError);
        _consent.Verify(c => c.ValidateForLoginAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
