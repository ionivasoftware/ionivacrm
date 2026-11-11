using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Customers.Rezerval;

/// <summary>
/// Writes a RezervAl company's access end date (<c>ExperationDate</c>) without touching iyzico — the EFT/Havale
/// contract path (11.11.2026: "EFT seçiyorum, iyzico ile işi yok"). RezervAl's <c>/Crm/Subscription</c> endpoint
/// always creates an iyzico product + plan and failed with "Aktif ürün bulunamadı" for EFT customers; the company
/// PUT carries the expiration directly. The PUT is a full form, so the current company record is read first and
/// sent back unchanged except for the date (RezervAl keeps Language/CountryPhoneCode defaults for TR tenants).
/// </summary>
public static class RezervalCompanyExpiration
{
    /// <summary>Sets the company's <c>ExperationDate</c>; throws <see cref="InvalidOperationException"/> when the company is not in RezervAl's list.</summary>
    public static async Task<RezervalCompany> SetAsync(
        ISaasBClient client, int rezervalCompanyId, DateTime expirationUtc, string apiKey, CancellationToken cancellationToken)
    {
        var companies = await client.GetRezervalCompaniesAsync(apiKey, cancellationToken);
        var company = companies.FirstOrDefault(c => c.Id == rezervalCompanyId && !c.IsDeleted)
            ?? throw new InvalidOperationException($"RezervAl'da {rezervalCompanyId} numaralı firma bulunamadı.");

        var form = new RezervalCompanyFormData
        {
            Name            = company.Name,
            Title           = company.Title ?? string.Empty,
            Phone           = company.Phone ?? string.Empty,
            Email           = company.Email ?? string.Empty,
            TaxUnit         = company.TaxUnit ?? string.Empty,
            TaxNumber       = company.TaxNumber ?? string.Empty,
            TCNo            = string.IsNullOrWhiteSpace(company.TcNo) ? null : company.TcNo,
            IsPersonCompany = !string.IsNullOrWhiteSpace(company.TcNo),
            Address         = company.Address ?? string.Empty,
            ExperationDate  = DateTime.SpecifyKind(expirationUtc, DateTimeKind.Utc),
        };
        await client.UpdateRezervalCompanyAsync(rezervalCompanyId, form, apiKey, cancellationToken);
        return company;
    }

    /// <summary>Access end for a contract: its end date, or ten years out for an open-ended one (matches <c>Customer.ExpirationDate</c>).</summary>
    public static DateTime AccessEndFor(DateTime startDateUtc, DateTime? endDateUtc) => endDateUtc ?? startDateUtc.AddYears(10);
}
