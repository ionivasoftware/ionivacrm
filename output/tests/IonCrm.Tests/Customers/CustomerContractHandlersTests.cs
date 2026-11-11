using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Commands.CancelCustomerContract;
using IonCrm.Application.Customers.Commands.CreateCustomerContract;
using IonCrm.Application.Customers.Rezerval;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace IonCrm.Tests.Customers;

/// <summary>
/// RezervAl contracts: EFT/Havale never touches iyzico (11.11.2026 — RezervAl's /Crm/Subscription failed with
/// "Aktif ürün bulunamadı" for an EFT customer); the company's access end date is written instead. Credit-card
/// contracts keep the iyzico subscription flow.
/// </summary>
public class CustomerContractHandlersTests
{
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<ICustomerContractRepository> _contracts = new();
    private readonly Mock<ISaasBClient> _saasB = new();
    private readonly Mock<ICurrentUserService> _user = new();

    private readonly Project _project = new() { Id = Guid.NewGuid(), Name = "RezervAl", RezervAlApiKey = "rez-key" };
    private readonly Customer _customer;
    private CustomerContract? _active;
    private readonly List<CustomerContract> _added = new();

    public CustomerContractHandlersTests()
    {
        _customer = new Customer { Id = Guid.NewGuid(), ProjectId = _project.Id, CompanyName = "Lezzet Restoran", LegacyId = "SAASB-5035", Status = CustomerStatus.Active };
        _customers.Setup(r => r.GetByIdAsync(_customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_customer);
        _projects.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _contracts.Setup(r => r.GetActiveByCustomerIdAsync(_customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _active);
        _contracts.Setup(r => r.AddAsync(It.IsAny<CustomerContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerContract c, CancellationToken _) => { _added.Add(c); return c; });
        _user.Setup(u => u.IsSuperAdmin).Returns(true);
        _saasB.Setup(s => s.GetRezervalCompaniesAsync("rez-key", It.IsAny<CancellationToken>())).ReturnsAsync(new List<RezervalCompany>
        {
            new(5035, "Lezzet Restoran", "Lezzet Gıda Ltd.", "0212 000 00 00", "info@lezzet.example", null, "Beyoğlu", "1234567890", null, "İstiklal Cd. 1", new DateTime(2026, 11, 1), new DateTime(2024, 1, 1), false, true),
            new(7, "Başka", null, null, null, null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow, false, null),
        });
    }

    private CreateCustomerContractCommandHandler CreateHandler() => new(_customers.Object, _projects.Object, _contracts.Object, _saasB.Object, _user.Object, NullLogger<CreateCustomerContractCommandHandler>.Instance);
    private CancelCustomerContractCommandHandler CancelHandler() => new(_customers.Object, _projects.Object, _contracts.Object, _saasB.Object, _user.Object, NullLogger<CancelCustomerContractCommandHandler>.Instance);

    [Fact]
    public async Task Eft_contract_skips_iyzico_and_writes_the_access_end_date_to_rezerval()
    {
        RezervalCompanyFormData? sent = null;
        _saasB.Setup(s => s.UpdateRezervalCompanyAsync(5035, It.IsAny<RezervalCompanyFormData>(), "rez-key", It.IsAny<CancellationToken>()))
            .Callback((int _, RezervalCompanyFormData f, string? _, CancellationToken _) => sent = f).Returns(Task.CompletedTask);

        var start = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        var result = await CreateHandler().Handle(new CreateCustomerContractCommand(_customer.Id, 1500m, ContractPaymentType.EftWire, start, 1), default);

        result.IsSuccess.Should().BeTrue(result.FirstError);
        _saasB.Verify(s => s.CreateRezervalSubscriptionAsync(It.IsAny<RezervalSubscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never, "EFT has nothing to do with iyzico");
        _saasB.Verify(s => s.UpgradeRezervalSubscriptionAsync(It.IsAny<RezervalUpgradeSubscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        sent.Should().NotBeNull();
        sent!.ExperationDate.Should().Be(new DateTime(2026, 12, 11, 0, 0, 0, DateTimeKind.Utc), "1-month contract ends a month after start");
        sent.Name.Should().Be("Lezzet Restoran");
        sent.Title.Should().Be("Lezzet Gıda Ltd.", "the current RezervAl record is sent back unchanged");
        sent.TaxUnit.Should().Be("Beyoğlu");

        var saved = _added.Should().ContainSingle().Subject;
        saved.PaymentType.Should().Be(ContractPaymentType.EftWire);
        saved.RezervalSubscriptionId.Should().BeNull();
        saved.EndDate.Should().Be(new DateTime(2026, 12, 11, 0, 0, 0, DateTimeKind.Utc));
        saved.NextInvoiceDate.Should().NotBeNull("EFT draft invoices are produced by the CRM");
        _customer.ExpirationDate.Should().Be(saved.EndDate);
    }

    [Fact]
    public async Task Eft_contract_with_open_end_sets_a_ten_year_access_end()
    {
        RezervalCompanyFormData? sent = null;
        _saasB.Setup(s => s.UpdateRezervalCompanyAsync(5035, It.IsAny<RezervalCompanyFormData>(), "rez-key", It.IsAny<CancellationToken>()))
            .Callback((int _, RezervalCompanyFormData f, string? _, CancellationToken _) => sent = f).Returns(Task.CompletedTask);
        var start = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

        var result = await CreateHandler().Handle(new CreateCustomerContractCommand(_customer.Id, 1500m, ContractPaymentType.EftWire, start, null), default);

        result.IsSuccess.Should().BeTrue(result.FirstError);
        sent!.ExperationDate.Should().Be(start.AddYears(10));
    }

    [Fact]
    public async Task Switching_a_live_card_subscription_to_eft_cancels_it_at_rezerval_first()
    {
        _active = new CustomerContract { Id = Guid.NewGuid(), CustomerId = _customer.Id, ProjectId = _project.Id, PaymentType = ContractPaymentType.CreditCard, Status = ContractStatus.Active, RezervalSubscriptionId = "sub-1", RezervalPaymentPlanId = "plan-1", StartDate = DateTime.UtcNow.AddMonths(-3) };
        _saasB.Setup(s => s.CancelRezervalSubscriptionAsync(It.Is<RezervalCancelSubscriptionRequest>(r => r.RezervalCompanyId == 5035), "rez-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RezervalCancelSubscriptionResponse(new RezervalCancelSubscriptionData(5035, new List<string>()), true, null));
        _saasB.Setup(s => s.UpdateRezervalCompanyAsync(5035, It.IsAny<RezervalCompanyFormData>(), "rez-key", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new CreateCustomerContractCommand(_customer.Id, 1200m, ContractPaymentType.EftWire, new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), 12), default);

        result.IsSuccess.Should().BeTrue(result.FirstError);
        _saasB.Verify(s => s.CancelRezervalSubscriptionAsync(It.IsAny<RezervalCancelSubscriptionRequest>(), "rez-key", It.IsAny<CancellationToken>()), Times.Once, "otherwise iyzico keeps charging the card");
        _saasB.Verify(s => s.UpgradeRezervalSubscriptionAsync(It.IsAny<RezervalUpgradeSubscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _active.Status.Should().Be(ContractStatus.Completed);
        _added.Should().ContainSingle().Which.RezervalSubscriptionId.Should().BeNull();
    }

    [Fact]
    public async Task Credit_card_contract_still_creates_the_iyzico_subscription_through_rezerval()
    {
        _saasB.Setup(s => s.CreateRezervalSubscriptionAsync(It.IsAny<RezervalSubscriptionRequest>(), "rez-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RezervalSubscriptionResponse(new RezervalSubscriptionData("sub-9", "plan-9", null), true, null));

        var result = await CreateHandler().Handle(new CreateCustomerContractCommand(_customer.Id, 1500m, ContractPaymentType.CreditCard, new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), 1), default);

        result.IsSuccess.Should().BeTrue(result.FirstError);
        _saasB.Verify(s => s.CreateRezervalSubscriptionAsync(It.Is<RezervalSubscriptionRequest>(r => r.PaymentType == "CreditCard" && r.DurationMonths == 1), "rez-key", It.IsAny<CancellationToken>()), Times.Once);
        _saasB.Verify(s => s.UpdateRezervalCompanyAsync(It.IsAny<int>(), It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _added.Should().ContainSingle().Which.RezervalSubscriptionId.Should().Be("sub-9");
    }

    [Fact]
    public async Task Eft_failure_at_rezerval_is_reported_in_turkish_and_nothing_is_saved()
    {
        _saasB.Setup(s => s.UpdateRezervalCompanyAsync(5035, It.IsAny<RezervalCompanyFormData>(), "rez-key", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HTTP 500 Internal Server Error"));

        var result = await CreateHandler().Handle(new CreateCustomerContractCommand(_customer.Id, 1500m, ContractPaymentType.EftWire, new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), 1), default);

        result.IsSuccess.Should().BeFalse();
        result.FirstError.Should().StartWith("Rezerval'da erişim bitiş tarihi güncellenemedi");
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_an_eft_only_contract_skips_the_iyzico_cancel_and_ends_access_today()
    {
        _active = new CustomerContract { Id = Guid.NewGuid(), CustomerId = _customer.Id, ProjectId = _project.Id, PaymentType = ContractPaymentType.EftWire, Status = ContractStatus.Active, StartDate = DateTime.UtcNow.AddMonths(-1), NextInvoiceDate = DateTime.UtcNow.AddDays(10) };
        RezervalCompanyFormData? sent = null;
        _saasB.Setup(s => s.UpdateRezervalCompanyAsync(5035, It.IsAny<RezervalCompanyFormData>(), "rez-key", It.IsAny<CancellationToken>()))
            .Callback((int _, RezervalCompanyFormData f, string? _, CancellationToken _) => sent = f).Returns(Task.CompletedTask);

        var result = await CancelHandler().Handle(new CancelCustomerContractCommand(_customer.Id), default);

        result.IsSuccess.Should().BeTrue(result.FirstError);
        _saasB.Verify(s => s.CancelRezervalSubscriptionAsync(It.IsAny<RezervalCancelSubscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never, "there is no iyzico subscription to cancel");
        sent!.ExperationDate.Should().Be(DateTime.UtcNow.Date);
        _active.Status.Should().Be(ContractStatus.Cancelled);
        _active.NextInvoiceDate.Should().BeNull();
    }

    [Fact]
    public async Task Cancelling_a_card_contract_still_goes_through_rezerval_cancel()
    {
        _active = new CustomerContract { Id = Guid.NewGuid(), CustomerId = _customer.Id, ProjectId = _project.Id, PaymentType = ContractPaymentType.CreditCard, Status = ContractStatus.Active, RezervalSubscriptionId = "sub-1", StartDate = DateTime.UtcNow.AddMonths(-1) };
        _saasB.Setup(s => s.CancelRezervalSubscriptionAsync(It.IsAny<RezervalCancelSubscriptionRequest>(), "rez-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RezervalCancelSubscriptionResponse(new RezervalCancelSubscriptionData(5035, new List<string> { "plan silinemedi" }), true, null));

        var result = await CancelHandler().Handle(new CancelCustomerContractCommand(_customer.Id), default);

        result.IsSuccess.Should().BeTrue(result.FirstError);
        result.Value!.IyzicoWarnings.Should().ContainSingle();
        _saasB.Verify(s => s.UpdateRezervalCompanyAsync(It.IsAny<int>(), It.IsAny<RezervalCompanyFormData>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _active.RezervalSubscriptionId.Should().BeNull();
    }

    [Fact]
    public async Task Expiration_helper_fails_clearly_when_the_company_is_not_in_rezerval()
    {
        var act = () => RezervalCompanyExpiration.SetAsync(_saasB.Object, 9999, DateTime.UtcNow, "rez-key", default);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*9999*");
    }
}
