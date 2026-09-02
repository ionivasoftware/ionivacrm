using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.SetPrimaryEmsUser;

/// <summary>Handles <see cref="SetPrimaryEmsUserCommand"/>.</summary>
public sealed class SetPrimaryEmsUserCommandHandler
    : IRequestHandler<SetPrimaryEmsUserCommand, Result<SetPrimaryEmsUserDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SetPrimaryEmsUserCommandHandler> _logger;

    public SetPrimaryEmsUserCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        ICurrentUserService currentUser,
        ILogger<SetPrimaryEmsUserCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasAClient        = saasAClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    public async Task<Result<SetPrimaryEmsUserDto>> Handle(
        SetPrimaryEmsUserCommand request,
        CancellationToken cancellationToken)
    {
        // Defense-in-depth: the controller is [Authorize(Policy = "SuperAdmin")], but re-assert here so
        // the command can never change a firm's owner from a non-SuperAdmin path (e.g. a future caller).
        if (!_currentUser.IsSuperAdmin)
            return Result<SetPrimaryEmsUserDto>.Failure("Bu işlem yalnızca süper admin tarafından yapılabilir.");

        if (string.IsNullOrWhiteSpace(request.UserId))
            return Result<SetPrimaryEmsUserDto>.Failure("Kullanıcı seçilmedi.");

        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<SetPrimaryEmsUserDto>.Failure("Müşteri bulunamadı.");

        // 2. Resolve EMS/Liftdesk company ID + credentials from the LegacyId + project.
        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        if (!SaasCustomerResolver.TryResolve(customer, project,
                out var emsCompanyId, out var emsApiKey, out var emsBaseUrl, out _))
        {
            return Result<SetPrimaryEmsUserDto>.Failure(
                "Bu müşteri EMS/Liftdesk kaynaklı değil. Ana kullanıcı yalnızca EMS/Liftdesk kaynaklı müşteriler için değiştirilebilir.");
        }

        // 3. Delegate the owner flip to the source. Membership + liveness are re-validated authoritatively
        //    on the source side (the CRM persists no firm-user rows to trust a client-supplied UserId).
        EmsSetPrimaryAdminResponse emsResponse;
        try
        {
            emsResponse = await _saasAClient.SetPrimaryAdminAsync(
                emsApiKey, emsCompanyId, request.UserId, cancellationToken, emsBaseUrl);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("EMS circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<SetPrimaryEmsUserDto>.Failure(
                "EMS API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EMS set-primary-admin failed for customer {CustomerId} (EMS company {EmsId}, user {UserId}).",
                customer.Id, emsCompanyId, request.UserId);
            return Result<SetPrimaryEmsUserDto>.Failure($"Ana kullanıcı değiştirilemedi: {ex.Message}");
        }

        // 4. Audit line — no audit entity exists in the domain yet, so a structured log is the record of
        //    "who changed the primary admin, for which firm, from whom to whom, when".
        _logger.LogInformation(
            "Primary admin changed by SuperAdmin {ActingUserId} for customer {CustomerId} (EMS company {EmsId}): {PreviousUserId} -> {NewUserId}.",
            _currentUser.UserId, customer.Id, emsCompanyId, emsResponse.PreviousUserId ?? "(unknown)", emsResponse.UserId);

        return Result<SetPrimaryEmsUserDto>.Success(new SetPrimaryEmsUserDto(
            CompanyId:      emsResponse.CompanyId,
            UserId:         emsResponse.UserId,
            PreviousUserId: emsResponse.PreviousUserId));
    }
}
