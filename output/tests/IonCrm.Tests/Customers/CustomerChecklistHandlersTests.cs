using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Customers.Commands.ResetCustomerChecklists;
using IonCrm.Application.Customers.Commands.UpdateCustomerChecklist;
using IonCrm.Application.Customers.Queries.GetCustomerChecklist;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Unit tests for the three Liftdesk checklist handlers (get / update / reset).
/// Covers: Liftdesk-only gating (LIFT- LegacyId), kind validation, missing project credentials,
/// tenant isolation, input trimming/validation on update and error mapping for client failures.
/// </summary>
public class CustomerChecklistHandlersTests
{
    private readonly Mock<ICustomerRepository>       _customerRepoMock = new();
    private readonly Mock<IProjectRepository>        _projectRepoMock  = new();
    private readonly Mock<ILiftdeskChecklistClient>  _clientMock       = new();
    private readonly Mock<ICurrentUserService>       _userMock         = new();

    private GetCustomerChecklistQueryHandler CreateGetHandler() => new(
        _customerRepoMock.Object, _projectRepoMock.Object, _clientMock.Object, _userMock.Object,
        Mock.Of<ILogger<GetCustomerChecklistQueryHandler>>());

    private UpdateCustomerChecklistCommandHandler CreateUpdateHandler() => new(
        _customerRepoMock.Object, _projectRepoMock.Object, _clientMock.Object, _userMock.Object,
        Mock.Of<ILogger<UpdateCustomerChecklistCommandHandler>>());

    private ResetCustomerChecklistsCommandHandler CreateResetHandler() => new(
        _customerRepoMock.Object, _projectRepoMock.Object, _clientMock.Object, _userMock.Object,
        Mock.Of<ILogger<ResetCustomerChecklistsCommandHandler>>());

    private (Guid customerId, Guid projectId) SetupLiftdeskCustomer(
        string legacyId = "LIFT-7",
        string? apiKey = "lift-key",
        string? baseUrl = "https://lift.example.com")
    {
        var projectId  = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var customer = new Customer
        {
            Id = customerId, ProjectId = projectId,
            CompanyName = "Liftdesk Firma", LegacyId = legacyId
        };
        var project = new Project
        {
            Id = projectId, LiftdeskApiKey = apiKey, LiftdeskBaseUrl = baseUrl
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });

        return (customerId, projectId);
    }

    private static LiftdeskChecklistDoc SampleDoc(string kind = "maintenance") => new(
        CompanyId: 7, Kind: kind, FormId: 4,
        Headers:
        [
            new LiftdeskChecklistHeader(Guid.NewGuid(), "Kuyu Kontrolü", 1, true,
            [
                new LiftdeskChecklistItem(Guid.NewGuid(), "Kuyu dibi temizliği", 1, true)
            ])
        ]);

    // ── GET ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_LiftdeskCustomer_ReturnsDoc()
    {
        var (customerId, _) = SetupLiftdeskCustomer();
        _clientMock
            .Setup(c => c.GetChecklistAsync("https://lift.example.com", "lift-key", 7, "maintenance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleDoc());

        var result = await CreateGetHandler().Handle(
            new GetCustomerChecklistQuery(customerId, "maintenance"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyId.Should().Be(7);
        result.Value.Headers.Should().HaveCount(1);
        result.Value.Headers[0].Title.Should().Be("Kuyu Kontrolü");
    }

    [Fact]
    public async Task Get_InvalidKind_FailsWithoutCallingClient()
    {
        var (customerId, _) = SetupLiftdeskCustomer();

        var result = await CreateGetHandler().Handle(
            new GetCustomerChecklistQuery(customerId, "both"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Geçersiz");
        _clientMock.Verify(c => c.GetChecklistAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Get_EmsCustomer_FailsAsNotLiftdesk()
    {
        var (customerId, _) = SetupLiftdeskCustomer(legacyId: "42"); // plain numeric = EMS

        var result = await CreateGetHandler().Handle(
            new GetCustomerChecklistQuery(customerId, "fault"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Liftdesk kaynaklı değil");
        _clientMock.Verify(c => c.GetChecklistAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Get_MissingCredentials_Fails()
    {
        var (customerId, _) = SetupLiftdeskCustomer(apiKey: null);

        var result = await CreateGetHandler().Handle(
            new GetCustomerChecklistQuery(customerId, "maintenance"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("tanımlı değil");
    }

    [Fact]
    public async Task Get_OtherTenant_FailsWithoutCallingClient()
    {
        var (customerId, _) = SetupLiftdeskCustomer();
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() }); // different project

        var result = await CreateGetHandler().Handle(
            new GetCustomerChecklistQuery(customerId, "maintenance"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("yetki");
        _clientMock.Verify(c => c.GetChecklistAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Get_CustomerNotFound_Fails()
    {
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(true);

        var result = await CreateGetHandler().Handle(
            new GetCustomerChecklistQuery(Guid.NewGuid(), "maintenance"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("bulunamadı");
    }

    [Fact]
    public async Task Get_Unauthorized401_MapsToKeyMessage()
    {
        var (customerId, _) = SetupLiftdeskCustomer();
        _clientMock
            .Setup(c => c.GetChecklistAsync(It.IsAny<string>(), It.IsAny<string>(), 7, "maintenance", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HTTP 401 Unauthorized", null, System.Net.HttpStatusCode.Unauthorized));

        var result = await CreateGetHandler().Handle(
            new GetCustomerChecklistQuery(customerId, "maintenance"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("anahtarı geçersiz");
    }

    // ── UPDATE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ValidInput_TrimsAndReturnsSavedDoc()
    {
        var (customerId, _) = SetupLiftdeskCustomer();

        LiftdeskChecklistUpdateRequest? sent = null;
        _clientMock
            .Setup(c => c.UpdateChecklistAsync(
                "https://lift.example.com", "lift-key", 7, "fault", null,
                It.IsAny<LiftdeskChecklistUpdateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, string, int?, LiftdeskChecklistUpdateRequest, CancellationToken>(
                (_, _, _, _, _, body, _) => sent = body)
            .ReturnsAsync(SampleDoc("fault"));

        var headers = new List<LiftdeskChecklistHeaderInput>
        {
            new("  Kuyu Kontrolü  ",
            [
                new LiftdeskChecklistItemInput(" Tampon kontrolü ", false)
            ])
        };

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerChecklistCommand(customerId, "fault", headers), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sent.Should().NotBeNull();
        sent!.Headers[0].Title.Should().Be("Kuyu Kontrolü");
        sent.Headers[0].Items[0].Text.Should().Be("Tampon kontrolü");
        sent.Headers[0].Items[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_BlankTitle_FailsWithoutCallingClient()
    {
        var (customerId, _) = SetupLiftdeskCustomer();

        var headers = new List<LiftdeskChecklistHeaderInput>
        {
            new("   ", [new LiftdeskChecklistItemInput("Madde", true)])
        };

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerChecklistCommand(customerId, "maintenance", headers), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Başlık");
        _clientMock.Verify(c => c.UpdateChecklistAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(),
            It.IsAny<LiftdeskChecklistUpdateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_BlankItemText_Fails()
    {
        var (customerId, _) = SetupLiftdeskCustomer();

        var headers = new List<LiftdeskChecklistHeaderInput>
        {
            new("Kuyu", [new LiftdeskChecklistItemInput("", true)])
        };

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerChecklistCommand(customerId, "maintenance", headers), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("boş madde");
    }

    [Fact]
    public async Task Update_EmptyHeaders_IsRejectedWithoutCallingClient()
    {
        // Liftdesk 400s on an empty set: zero rows read as "never seeded", so the app would restore
        // the default on the next read. Emptying is only possible through reset.
        var (customerId, _) = SetupLiftdeskCustomer();

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerChecklistCommand(customerId, "maintenance", []), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("en az bir başlık");
        _clientMock.Verify(c => c.UpdateChecklistAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(),
            It.IsAny<LiftdeskChecklistUpdateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_InvalidKind_Fails()
    {
        var (customerId, _) = SetupLiftdeskCustomer();

        var result = await CreateUpdateHandler().Handle(
            new UpdateCustomerChecklistCommand(customerId, "both", []), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Geçersiz");
    }

    // ── RESET ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_Both_ReturnsBothDocs()
    {
        var (customerId, _) = SetupLiftdeskCustomer();
        _userMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _clientMock
            .Setup(c => c.ResetChecklistsAsync("https://lift.example.com", "lift-key", 7, "both", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskChecklistResetResponse(7, SampleDoc("maintenance"), SampleDoc("fault")));

        var result = await CreateResetHandler().Handle(
            new ResetCustomerChecklistsCommand(customerId, "both"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Maintenance.Should().NotBeNull();
        result.Value.Fault.Should().NotBeNull();
    }

    [Fact]
    public async Task Reset_SingleKind_PassesKindThrough()
    {
        var (customerId, _) = SetupLiftdeskCustomer();
        _userMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _clientMock
            .Setup(c => c.ResetChecklistsAsync(It.IsAny<string>(), It.IsAny<string>(), 7, "maintenance", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskChecklistResetResponse(7, SampleDoc("maintenance"), null));

        var result = await CreateResetHandler().Handle(
            new ResetCustomerChecklistsCommand(customerId, "maintenance"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Maintenance.Should().NotBeNull();
        result.Value.Fault.Should().BeNull();
        _clientMock.Verify(c => c.ResetChecklistsAsync(
            It.IsAny<string>(), It.IsAny<string>(), 7, "maintenance", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reset_InvalidKind_Fails()
    {
        var (customerId, _) = SetupLiftdeskCustomer();

        var result = await CreateResetHandler().Handle(
            new ResetCustomerChecklistsCommand(customerId, "all"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Geçersiz");
        _clientMock.Verify(c => c.ResetChecklistsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Wire format: Liftdesk serialises enums as NAMES ────────────────────────

    private static System.Text.Json.JsonSerializerOptions WireOpts() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Liftdesk's global JsonStringEnumConverter writes ChecklistType as "Elevator"/"Escalator".
    /// Modelling it as a bare int broke the entire checklist screen with
    /// "The JSON value could not be converted ... Path: $.type" — both shapes must deserialize.
    /// </summary>
    [Theory]
    [InlineData("\"Elevator\"", 1)]
    [InlineData("\"Escalator\"", 2)]
    [InlineData("\"escalator\"", 2)]   // case-insensitive
    [InlineData("1", 1)]               // numeric, should the wire format ever change back
    [InlineData("2", 2)]
    [InlineData("\"2\"", 2)]           // numeric-as-string
    [InlineData("\"Unknown\"", 1)]     // unknown → elevator, the backfill default
    public void ChecklistDoc_TypeField_DeserializesFromNameOrNumber(string typeJson, int expected)
    {
        var json =
            "{\"companyId\":7,\"kind\":\"maintenance\",\"formId\":4,\"type\":" + typeJson + "," +
            "\"headers\":[{\"id\":\"9a1c0000-0000-4000-8000-000000000001\",\"title\":\"Kuyu\"," +
            "\"sortOrder\":1,\"isActive\":true,\"items\":[]}]}";

        var doc = System.Text.Json.JsonSerializer.Deserialize<LiftdeskChecklistDoc>(json, WireOpts())!;

        doc.Type.Should().Be(expected);
        doc.Headers.Should().HaveCount(1);
    }

    [Fact]
    public void ChecklistDoc_TypeFieldAbsent_DefaultsToElevator()
    {
        var json = "{\"companyId\":7,\"kind\":\"fault\",\"formId\":4,\"headers\":[]}";

        var doc = System.Text.Json.JsonSerializer.Deserialize<LiftdeskChecklistDoc>(json, WireOpts())!;

        doc.Type.Should().Be(LiftdeskChecklistType.Elevator);
    }

    /// <summary>The reset response embeds the same docs, including the escalator list.</summary>
    [Fact]
    public void ResetResponse_WithNamedTypes_Deserializes()
    {
        var json =
            "{\"companyId\":7," +
            "\"maintenance\":{\"companyId\":7,\"kind\":\"maintenance\",\"formId\":4,\"type\":\"Elevator\",\"headers\":[]}," +
            "\"escalatorMaintenance\":{\"companyId\":7,\"kind\":\"maintenance\",\"formId\":4,\"type\":\"Escalator\",\"headers\":[]}," +
            "\"fault\":{\"companyId\":7,\"kind\":\"fault\",\"formId\":4,\"type\":\"Elevator\",\"headers\":[]}}";

        var result = System.Text.Json.JsonSerializer.Deserialize<LiftdeskChecklistResetResponse>(json, WireOpts())!;

        result.Maintenance!.Type.Should().Be(LiftdeskChecklistType.Elevator);
        result.EscalatorMaintenance!.Type.Should().Be(LiftdeskChecklistType.Escalator);
        result.Fault.Should().NotBeNull();
    }

    // ── Wire-format defaults ──────────────────────────────────────────────────

    [Fact]
    public void HeaderInput_IsActiveOmittedInJson_DefaultsToTrue()
    {
        // Contract 2.3: isActive is optional and defaults to TRUE — for headers AND items.
        var json = """{"title":"Kuyu","items":[{"text":"Madde"}]}""";
        var opts = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        };

        var header = System.Text.Json.JsonSerializer.Deserialize<LiftdeskChecklistHeaderInput>(json, opts)!;

        header.IsActive.Should().BeTrue();
        header.Items.Should().HaveCount(1);
        header.Items[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_NonLiftdeskCustomer_Fails()
    {
        var (customerId, _) = SetupLiftdeskCustomer(legacyId: "REZV-5");

        var result = await CreateResetHandler().Handle(
            new ResetCustomerChecklistsCommand(customerId, "both"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Liftdesk kaynaklı değil");
    }
}
