using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.ResetCustomerChecklists;

/// <summary>Handles <see cref="ResetCustomerChecklistsCommand"/>.</summary>
public sealed class ResetCustomerChecklistsCommandHandler
    : IRequestHandler<ResetCustomerChecklistsCommand, Result<LiftdeskChecklistResetResponse>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ILiftdeskChecklistClient _checklistClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ResetCustomerChecklistsCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="ResetCustomerChecklistsCommandHandler"/>.</summary>
    public ResetCustomerChecklistsCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ILiftdeskChecklistClient checklistClient,
        ICurrentUserService currentUser,
        ILogger<ResetCustomerChecklistsCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _checklistClient    = checklistClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<LiftdeskChecklistResetResponse>> Handle(
        ResetCustomerChecklistsCommand request,
        CancellationToken cancellationToken)
    {
        if (!LiftdeskChecklistHelper.IsValidKind(request.Kind, allowBoth: true))
            return Result<LiftdeskChecklistResetResponse>.Failure(
                "Geçersiz checklist türü. 'maintenance', 'fault' veya 'both' olmalıdır.");

        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<LiftdeskChecklistResetResponse>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<LiftdeskChecklistResetResponse>.Failure("Bu müşteriye erişim yetkiniz yok.");

        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var resolveError = LiftdeskChecklistHelper.TryResolveLiftdesk(
            customer, project, out var companyId, out var apiKey, out var baseUrl);
        if (resolveError is not null)
            return Result<LiftdeskChecklistResetResponse>.Failure(resolveError);

        try
        {
            var response = await _checklistClient.ResetChecklistsAsync(
                baseUrl, apiKey, companyId, request.Kind, cancellationToken);

            // Destructive operation — always log who reset what.
            _logger.LogWarning(
                "Liftdesk checklist RESET ({Kind}) for customer {CustomerId} (Liftdesk company {CompanyId}) by user {UserId}.",
                request.Kind, customer.Id, companyId, _currentUser.UserId);

            return Result<LiftdeskChecklistResetResponse>.Success(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Liftdesk checklist reset ({Kind}) failed for customer {CustomerId} (Liftdesk company {CompanyId}).",
                request.Kind, customer.Id, companyId);
            return Result<LiftdeskChecklistResetResponse>.Failure(LiftdeskChecklistHelper.DescribeFailure(ex));
        }
    }
}
