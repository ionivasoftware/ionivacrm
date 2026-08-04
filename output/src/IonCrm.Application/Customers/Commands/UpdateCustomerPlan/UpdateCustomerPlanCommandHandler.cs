using System.Net;
using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.UpdateCustomerPlan;

/// <summary>Handles <see cref="UpdateCustomerPlanCommand"/>.</summary>
public sealed class UpdateCustomerPlanCommandHandler
    : IRequestHandler<UpdateCustomerPlanCommand, Result<LiftdeskCompanyPlan>>
{
    /// <summary>Tier names Liftdesk accepts (the plan NAME is also accepted, e.g. "EMS Pro").</summary>
    private static readonly string[] KnownTiers = ["Standart", "Pro", "Prime"];

    /// <summary>Billing periods Liftdesk accepts.</summary>
    private static readonly string[] KnownBillingPeriods = ["Monthly", "Yearly"];

    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ILiftdeskPlanClient _planClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateCustomerPlanCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="UpdateCustomerPlanCommandHandler"/>.</summary>
    public UpdateCustomerPlanCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ILiftdeskPlanClient planClient,
        ICurrentUserService currentUser,
        ILogger<UpdateCustomerPlanCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _planClient         = planClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<LiftdeskCompanyPlan>> Handle(
        UpdateCustomerPlanCommand request,
        CancellationToken cancellationToken)
    {
        var tier = string.IsNullOrWhiteSpace(request.Tier) ? null : request.Tier.Trim();
        var billingPeriod = string.IsNullOrWhiteSpace(request.BillingPeriod) ? null : request.BillingPeriod.Trim();

        // Validate before spending a round-trip; Liftdesk would 400 on the same inputs.
        if (tier is null && request.PlanId is null)
            return Result<LiftdeskCompanyPlan>.Failure("Paket seçilmedi. 'tier' veya 'planId' gönderilmelidir.");

        if (request.PlanId is null && tier is not null
            && !KnownTiers.Contains(tier, StringComparer.OrdinalIgnoreCase)
            // A plan NAME is valid too, so only reject a bare tier that is clearly not one of ours.
            && !tier.Contains(' '))
        {
            return Result<LiftdeskCompanyPlan>.Failure(
                "Geçersiz paket kademesi. 'Standart', 'Pro' veya 'Prime' olmalıdır.");
        }

        if (billingPeriod is not null && !KnownBillingPeriods.Contains(billingPeriod, StringComparer.OrdinalIgnoreCase))
            return Result<LiftdeskCompanyPlan>.Failure("Geçersiz ödeme dönemi. 'Monthly' veya 'Yearly' olmalıdır.");

        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<LiftdeskCompanyPlan>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<LiftdeskCompanyPlan>.Failure("Bu müşteriye erişim yetkiniz yok.");

        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var resolveError = LiftdeskCustomerHelper.TryResolveLiftdesk(
            customer, project, out var companyId, out var apiKey, out var baseUrl);
        if (resolveError is not null)
            return Result<LiftdeskCompanyPlan>.Failure(resolveError);

        try
        {
            var plan = await _planClient.UpdatePlanAsync(
                baseUrl, apiKey, companyId,
                new LiftdeskPlanChangeRequest(tier, request.PlanId, billingPeriod),
                cancellationToken);

            // Tier drives feature gating for a whole tenant — always log who changed what.
            _logger.LogWarning(
                "Liftdesk plan changed for customer {CustomerId} (Liftdesk company {CompanyId}) to Tier={Tier} PlanId={PlanId} Period={Period} by user {UserId}.",
                customer.Id, companyId, tier, request.PlanId, billingPeriod, _currentUser.UserId);

            return Result<LiftdeskCompanyPlan>.Success(plan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // 409 = the tenant has no subscription row at all; a tier means nothing without a period.
            _logger.LogWarning(ex,
                "Liftdesk plan change rejected for customer {CustomerId} (Liftdesk company {CompanyId}): no subscription.",
                customer.Id, companyId);
            return Result<LiftdeskCompanyPlan>.Failure(
                "Firmanın abonelik kaydı yok. Önce \"Süre Uzat\" ile lisans süresi tanımlayın, sonra paketi değiştirin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Liftdesk plan change failed for customer {CustomerId} (Liftdesk company {CompanyId}).",
                customer.Id, companyId);
            return Result<LiftdeskCompanyPlan>.Failure(LiftdeskCustomerHelper.DescribeFailure(ex));
        }
    }
}
