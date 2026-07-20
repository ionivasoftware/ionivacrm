using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetCustomerEmsSummary;

/// <summary>Handles <see cref="GetCustomerEmsSummaryQuery"/>.</summary>
public sealed class GetCustomerEmsSummaryQueryHandler
    : IRequestHandler<GetCustomerEmsSummaryQuery, Result<EmsSummaryDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCustomerEmsSummaryQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCustomerEmsSummaryQueryHandler"/>.</summary>
    public GetCustomerEmsSummaryQueryHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        ICurrentUserService currentUser,
        ILogger<GetCustomerEmsSummaryQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasAClient        = saasAClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<EmsSummaryDto>> Handle(
        GetCustomerEmsSummaryQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<EmsSummaryDto>.Failure("Müşteri bulunamadı.");

        // 2. Tenant authorization
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<EmsSummaryDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 3. Resolve EMS/Liftdesk company ID + credentials from the LegacyId + project.
        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        if (!SaasCustomerResolver.TryResolve(customer, project,
                out var emsCompanyId, out var emsApiKey, out var emsBaseUrl, out _))
        {
            return Result<EmsSummaryDto>.Failure(
                "Bu müşteri EMS/Liftdesk kaynaklı değil. Kullanım özeti yalnızca EMS/Liftdesk kaynaklı müşteriler için sorgulanabilir.");
        }

        // 5. Call EMS/Liftdesk API
        Common.Models.ExternalApis.EmsCompanySummaryResponse emsSummary;
        try
        {
            emsSummary = await _saasAClient.GetCompanySummaryAsync(
                emsApiKey, emsCompanyId, cancellationToken, emsBaseUrl);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("EMS circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<EmsSummaryDto>.Failure(
                "EMS API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EMS GetCompanySummary failed for customer {CustomerId} (EMS company {EmsId}).",
                customer.Id, emsCompanyId);
            return Result<EmsSummaryDto>.Failure(
                $"EMS'ten kullanım özeti alınamadı: {ex.Message}");
        }

        // 6. Map to DTOs
        var totals = new EmsSummaryTotalsDto(
            emsSummary.Totals.CustomerCount,
            emsSummary.Totals.ElevatorCount,
            emsSummary.Totals.UserCount);

        var monthly = emsSummary.Monthly
            .Select(m => new EmsSummaryMonthlyStatDto(
                m.Year,
                m.Month,
                m.MaintenanceCount,
                m.FaultCount,
                m.PartChangeOfferCount + m.RevisionOfferCount + m.AssemblyOfferCount))
            .ToList();

        var dto = new EmsSummaryDto(emsCompanyId, totals, monthly);

        _logger.LogInformation(
            "Fetched EMS summary for customer {CustomerId} (EMS company {EmsId}). Elevators={ElevatorCount} Users={UserCount}",
            customer.Id, emsCompanyId,
            emsSummary.Totals.ElevatorCount,
            emsSummary.Totals.UserCount);

        return Result<EmsSummaryDto>.Success(dto);
    }
}
