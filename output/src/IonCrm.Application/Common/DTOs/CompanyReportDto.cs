namespace IonCrm.Application.Common.DTOs;

/// <summary>
/// Raporlar → "Firma ve Lead Raporları": one project's customers by status and country plus the competitor
/// picture (which of our companies work with another vendor, and with whom). Internal/test firms are excluded.
/// </summary>
public record CompanyReportDto(
    StatusCountsDto Totals,
    IReadOnlyList<CountryCompanyCountsDto> ByCountry,
    /// <summary>Companies whose country came from the phone's international prefix rather than the record.</summary>
    int CountryFromPhone,
    /// <summary>Companies with no country on record and no international phone — counted under <see cref="DefaultCountry"/>.</summary>
    int CountryDefaulted,
    /// <summary>ISO2 the unresolved companies were counted under (TR, the home market).</summary>
    string DefaultCountry,
    /// <summary>Companies whose latest contact said they work with another vendor.</summary>
    int UsingCompetitor,
    /// <summary>…of which the vendor was not named.</summary>
    int UsingCompetitorUnnamed,
    IReadOnlyList<CompetitorUsageDto> Competitors,
    IReadOnlyList<CompetitorCustomerDto> CompetitorCustomers);

public record StatusCountsDto(int Lead, int Active, int Demo, int Churned, int Passive, int Total);

/// <summary><paramref name="Country"/> is ISO2; unresolved firms are counted under the default country, so it is never null in practice (nullable kept for the wire shape).</summary>
public record CountryCompanyCountsDto(string? Country, int Lead, int Active, int Demo, int Churned, int Passive, int Total);

public record CountryCountDto(string? Country, int Count);

/// <summary>One competitor row; <paramref name="CompetitorId"/> null = "works with another vendor, name not given".</summary>
public record CompetitorUsageDto(
    Guid? CompetitorId,
    string Name,
    string? Website,
    int Lead,
    int Active,
    int Demo,
    int Churned,
    int Passive,
    int Total,
    IReadOnlyList<CountryCountDto> Countries);

/// <summary>A company that works with another vendor, with the most recent contact record that said so.</summary>
public record CompetitorCustomerDto(
    Guid CustomerId,
    string CompanyName,
    string Status,
    string? Country,
    string? CountrySource,
    Guid? CompetitorId,
    string? CompetitorName,
    DateTime? LastContactAt);
