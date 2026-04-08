using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetCustomerRezervalSummary;

/// <summary>Handles <see cref="GetCustomerRezervalSummaryQuery"/>.</summary>
public sealed class GetCustomerRezervalSummaryQueryHandler
    : IRequestHandler<GetCustomerRezervalSummaryQuery, Result<RezervalSummaryDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasBClient _saasBClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCustomerRezervalSummaryQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCustomerRezervalSummaryQueryHandler"/>.</summary>
    public GetCustomerRezervalSummaryQueryHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasBClient saasBClient,
        ICurrentUserService currentUser,
        ILogger<GetCustomerRezervalSummaryQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasBClient        = saasBClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<RezervalSummaryDto>> Handle(
        GetCustomerRezervalSummaryQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<RezervalSummaryDto>.Failure("Müşteri bulunamadı.");

        // 2. Tenant authorization
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<RezervalSummaryDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 3. Verify Rezerval-sourced customer and extract numeric company ID.
        //    Accepted LegacyId formats:
        //      "SAASB-{id}" → synced from Rezerval (legacy prefix)
        //      "REZV-{id}"  → created in CRM and pushed to Rezerval
        if (string.IsNullOrEmpty(customer.LegacyId)
            || (!customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
             && !customer.LegacyId.StartsWith("REZV-", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<RezervalSummaryDto>.Failure(
                "Bu müşteri Rezerval kaynaklı değil. Rezerval özeti yalnızca Rezerval müşterileri için sorgulanabilir.");
        }

        string rawId = customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
            ? customer.LegacyId["SAASB-".Length..]
            : customer.LegacyId["REZV-".Length..];

        if (!int.TryParse(rawId, out var rezervalCompanyId))
        {
            return Result<RezervalSummaryDto>.Failure(
                "Müşterinin Rezerval şirket numarası okunamadı.");
        }

        // 4. Resolve Rezerval credentials
        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var rezervalApiKey = project?.RezervAlApiKey;
        if (string.IsNullOrWhiteSpace(rezervalApiKey))
        {
            return Result<RezervalSummaryDto>.Failure(
                "Bu projede Rezerval API anahtarı yapılandırılmamış.");
        }

        // 5. Call Rezerval API
        RezervalCompanySummaryResponse rezervalResponse;
        try
        {
            rezervalResponse = await _saasBClient.GetCompanySummaryAsync(
                rezervalCompanyId, rezervalApiKey, cancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rezerval circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<RezervalSummaryDto>.Failure(
                "Rezerval API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rezerval GetCompanySummary failed for customer {CustomerId} (Rezerval company {RezvId}).",
                customer.Id, rezervalCompanyId);
            return Result<RezervalSummaryDto>.Failure(
                $"Rezerval'den şirket özeti alınamadı: {ex.Message}");
        }

        if (rezervalResponse.Data is null)
        {
            return Result<RezervalSummaryDto>.Failure("Rezerval boş özet döndü.");
        }

        var dto = new RezervalSummaryDto(
            RezervalCompanyId: rezervalCompanyId,
            CompanyName:       rezervalResponse.Data.CompanyName,
            LastWeek:          MapPeriod(rezervalResponse.Data.LastWeek),
            LastMonth:         MapPeriod(rezervalResponse.Data.LastMonth),
            Last3Months:       MapPeriod(rezervalResponse.Data.Last3Months));

        _logger.LogInformation(
            "Fetched Rezerval summary for customer {CustomerId} (Rezerval company {RezvId}).",
            customer.Id, rezervalCompanyId);

        return Result<RezervalSummaryDto>.Success(dto);
    }

    private static RezervalSummaryPeriodDto? MapPeriod(RezervalSummaryPeriod? period) =>
        period is null
            ? null
            : new RezervalSummaryPeriodDto(
                StartDate:                 period.StartDate,
                EndDate:                   period.EndDate,
                ReservationCount:          period.ReservationCount,
                PersonCount:               period.PersonCount,
                CompletedReservationCount: period.CompletedReservationCount,
                CancelledReservationCount: period.CancelledReservationCount,
                OnlineReservationCount:    period.OnlineReservationCount,
                WalkInCount:               period.WalkInCount,
                WalkInPersonCount:         period.WalkInPersonCount,
                SmsSentCount:              period.SmsSentCount);
}
