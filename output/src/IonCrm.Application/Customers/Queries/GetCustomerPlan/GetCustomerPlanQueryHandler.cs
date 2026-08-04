using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetCustomerPlan;

/// <summary>Handles <see cref="GetCustomerPlanQuery"/>.</summary>
public sealed class GetCustomerPlanQueryHandler
    : IRequestHandler<GetCustomerPlanQuery, Result<LiftdeskCompanyPlan>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ILiftdeskPlanClient _planClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCustomerPlanQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCustomerPlanQueryHandler"/>.</summary>
    public GetCustomerPlanQueryHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ILiftdeskPlanClient planClient,
        ICurrentUserService currentUser,
        ILogger<GetCustomerPlanQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _planClient         = planClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<LiftdeskCompanyPlan>> Handle(
        GetCustomerPlanQuery request,
        CancellationToken cancellationToken)
    {
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
            var plan = await _planClient.GetPlanAsync(baseUrl, apiKey, companyId, cancellationToken);
            return Result<LiftdeskCompanyPlan>.Success(plan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Liftdesk plan fetch failed for customer {CustomerId} (Liftdesk company {CompanyId}).",
                customer.Id, companyId);
            return Result<LiftdeskCompanyPlan>.Failure(LiftdeskCustomerHelper.DescribeFailure(ex));
        }
    }
}
