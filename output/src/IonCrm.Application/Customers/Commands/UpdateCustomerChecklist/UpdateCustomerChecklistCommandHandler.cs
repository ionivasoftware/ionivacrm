using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.UpdateCustomerChecklist;

/// <summary>Handles <see cref="UpdateCustomerChecklistCommand"/>.</summary>
public sealed class UpdateCustomerChecklistCommandHandler
    : IRequestHandler<UpdateCustomerChecklistCommand, Result<LiftdeskChecklistDoc>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ILiftdeskChecklistClient _checklistClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateCustomerChecklistCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="UpdateCustomerChecklistCommandHandler"/>.</summary>
    public UpdateCustomerChecklistCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ILiftdeskChecklistClient checklistClient,
        ICurrentUserService currentUser,
        ILogger<UpdateCustomerChecklistCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _checklistClient    = checklistClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<LiftdeskChecklistDoc>> Handle(
        UpdateCustomerChecklistCommand request,
        CancellationToken cancellationToken)
    {
        if (!LiftdeskChecklistHelper.IsValidKind(request.Kind))
            return Result<LiftdeskChecklistDoc>.Failure("Geçersiz checklist türü. 'maintenance' veya 'fault' olmalıdır.");

        if (request.Headers is null)
            return Result<LiftdeskChecklistDoc>.Failure("Checklist başlıkları (headers) eksik.");

        // Trim and validate before calling Liftdesk — blank titles/texts would 400 there anyway.
        var headers = new List<LiftdeskChecklistHeaderInput>(request.Headers.Count);
        foreach (var header in request.Headers)
        {
            var title = header.Title?.Trim();
            if (string.IsNullOrEmpty(title))
                return Result<LiftdeskChecklistDoc>.Failure("Başlık adı boş olamaz.");

            var items = new List<LiftdeskChecklistItemInput>(header.Items?.Count ?? 0);
            foreach (var item in header.Items ?? [])
            {
                var text = item.Text?.Trim();
                if (string.IsNullOrEmpty(text))
                    return Result<LiftdeskChecklistDoc>.Failure($"\"{title}\" başlığında boş madde metni var.");
                items.Add(new LiftdeskChecklistItemInput(text, item.IsActive));
            }

            headers.Add(new LiftdeskChecklistHeaderInput(title, items, header.IsActive));
        }

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
            var doc = await _checklistClient.UpdateChecklistAsync(
                baseUrl, apiKey, companyId, request.Kind,
                new LiftdeskChecklistUpdateRequest(headers), cancellationToken);

            _logger.LogInformation(
                "Liftdesk {Kind} checklist updated for customer {CustomerId} (Liftdesk company {CompanyId}, {HeaderCount} headers).",
                request.Kind, customer.Id, companyId, headers.Count);

            return Result<LiftdeskChecklistDoc>.Success(doc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Liftdesk {Kind} checklist update failed for customer {CustomerId} (Liftdesk company {CompanyId}).",
                request.Kind, customer.Id, companyId);
            return Result<LiftdeskChecklistDoc>.Failure(LiftdeskChecklistHelper.DescribeFailure(ex));
        }
    }
}
