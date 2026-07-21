using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetCustomerChecklist;

/// <summary>Handles <see cref="GetCustomerChecklistQuery"/>.</summary>
public sealed class GetCustomerChecklistQueryHandler
    : IRequestHandler<GetCustomerChecklistQuery, Result<LiftdeskChecklistDoc>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ILiftdeskChecklistClient _checklistClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCustomerChecklistQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCustomerChecklistQueryHandler"/>.</summary>
    public GetCustomerChecklistQueryHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ILiftdeskChecklistClient checklistClient,
        ICurrentUserService currentUser,
        ILogger<GetCustomerChecklistQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _checklistClient    = checklistClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<LiftdeskChecklistDoc>> Handle(
        GetCustomerChecklistQuery request,
        CancellationToken cancellationToken)
    {
        if (!LiftdeskChecklistHelper.IsValidKind(request.Kind))
            return Result<LiftdeskChecklistDoc>.Failure("Geçersiz checklist türü. 'maintenance' veya 'fault' olmalıdır.");

        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<LiftdeskChecklistDoc>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<LiftdeskChecklistDoc>.Failure("Bu müşteriye erişim yetkiniz yok.");

        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var resolveError = LiftdeskChecklistHelper.TryResolveLiftdesk(
            customer, project, out var companyId, out var apiKey, out var baseUrl);
        if (resolveError is not null)
            return Result<LiftdeskChecklistDoc>.Failure(resolveError);

        try
        {
            var doc = await _checklistClient.GetChecklistAsync(
                baseUrl, apiKey, companyId, request.Kind, cancellationToken);
            return Result<LiftdeskChecklistDoc>.Success(doc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Liftdesk {Kind} checklist fetch failed for customer {CustomerId} (Liftdesk company {CompanyId}).",
                request.Kind, customer.Id, companyId);
            return Result<LiftdeskChecklistDoc>.Failure(LiftdeskChecklistHelper.DescribeFailure(ex));
        }
    }
}
