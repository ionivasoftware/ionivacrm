using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.PushCustomerToRezerval;

/// <summary>
/// Pushes a CRM customer to the RezervAl system.
/// If the customer's LegacyId starts with "REZV-" an update is performed (PUT);
/// otherwise a new company is created (POST) and the returned companyId is stored
/// as "REZV-{companyId}" in Customer.LegacyId.
/// </summary>
public record PushCustomerToRezervalCommand : IRequest<Result<PushCustomerToRezervalDto>>
{
    /// <summary>Gets or sets the CRM customer ID.</summary>
    public Guid CustomerId { get; init; }

    // ── Company fields ────────────────────────────────────────────────────────

    /// <summary>Gets or sets the company/firm name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets or sets the company title/type (e.g. "Ltd. Şti.").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets or sets the company phone number.</summary>
    public string Phone { get; init; } = string.Empty;

    /// <summary>Gets or sets the company e-mail address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets or sets the tax office name.</summary>
    public string TaxUnit { get; init; } = string.Empty;

    /// <summary>Gets or sets the tax identification number.</summary>
    public string TaxNumber { get; init; } = string.Empty;

    /// <summary>Gets or sets the Turkish national ID (T.C. Kimlik No). Optional.</summary>
    public string? TCNo { get; init; }

    /// <summary>Gets or sets a value indicating whether this is an individual (person) company.</summary>
    public bool IsPersonCompany { get; init; }

    /// <summary>Gets or sets the company address.</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>Gets or sets the interface language code. Defaults to 1 (Turkish).</summary>
    public int Language { get; init; } = 1;

    /// <summary>Gets or sets the country phone code. Defaults to 90 (Turkey).</summary>
    public int CountryPhoneCode { get; init; } = 90;

    /// <summary>Gets or sets the subscription expiration date (RezervAl field: ExperationDate).</summary>
    public DateTime? ExperationDate { get; init; }

    // ── Admin account fields ──────────────────────────────────────────────────

    /// <summary>Gets or sets the admin user full name.</summary>
    public string AdminNameSurname { get; init; } = string.Empty;

    /// <summary>Gets or sets the admin user login/username.</summary>
    public string AdminLoginName { get; init; } = string.Empty;

    /// <summary>Gets or sets the admin user password.</summary>
    public string AdminPassword { get; init; } = string.Empty;

    /// <summary>Gets or sets the admin user e-mail address.</summary>
    public string AdminEmail { get; init; } = string.Empty;

    /// <summary>Gets or sets the admin user phone number.</summary>
    public string AdminPhone { get; init; } = string.Empty;

    // ── Optional logo ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the logo image encoded as a Base64 string. Optional.
    /// Decoded to bytes before sending as a multipart/form-data file part.
    /// </summary>
    public string? LogoBase64 { get; init; }

    /// <summary>Gets or sets the logo file name (e.g. "logo.png"). Used in the multipart part header.</summary>
    public string? LogoFileName { get; init; }
}

/// <summary>Result returned after a successful push to RezervAl.</summary>
public record PushCustomerToRezervalDto(
    /// <summary>The RezervAl company ID (integer).</summary>
    int RezervalCompanyId,
    /// <summary>The LegacyId stored in the CRM customer record ("REZV-{id}").</summary>
    string LegacyId,
    /// <summary>Indicates whether a new company was created (true) or an existing one was updated (false).</summary>
    bool WasCreated);
