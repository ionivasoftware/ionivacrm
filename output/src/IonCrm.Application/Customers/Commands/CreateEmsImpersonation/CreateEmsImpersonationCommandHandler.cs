using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.CreateEmsImpersonation;

/// <summary>Handles <see cref="CreateEmsImpersonationCommand"/>.</summary>
public sealed class CreateEmsImpersonationCommandHandler
    : IRequestHandler<CreateEmsImpersonationCommand, Result<EmsImpersonationDto>>
{
    private const int ReasonMin = 10;
    private const int ReasonMax = 500;
    private const int AgentMax  = 200;

    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateEmsImpersonationCommandHandler> _logger;

    public CreateEmsImpersonationCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        ICurrentUserService currentUser,
        ILogger<CreateEmsImpersonationCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasAClient        = saasAClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    public async Task<Result<EmsImpersonationDto>> Handle(
        CreateEmsImpersonationCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return Result<EmsImpersonationDto>.Failure("Kullanıcı seçilmedi.");

        // Gerekçe sözleşme gereği kırpılmış hâliyle 10–500 karakter; Liftdesk denetim kaydının
        // "neden" sorusunu bu yanıtlar. Sunucuda da doğrulanır ki istemci atlayamasın.
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length < ReasonMin || reason.Length > ReasonMax)
            return Result<EmsImpersonationDto>.Failure(
                $"Gerekçe {ReasonMin}–{ReasonMax} karakter olmalı (şu an {reason.Length}).");

        // agent = oturum açmış CRM operatörü. İstemciden gelen hiçbir değere güvenilmez: Liftdesk
        // belirtece act.sub olarak yazar ve denetim kaydı "kim girdi" sorusunu buradan cevaplar.
        var agent = (_currentUser.Email ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(agent))
            return Result<EmsImpersonationDto>.Failure("Operatör kimliği belirlenemedi; yeniden giriş yapın.");
        if (agent.Length > AgentMax) agent = agent[..AgentMax];

        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<EmsImpersonationDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<EmsImpersonationDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        if (!SaasCustomerResolver.TryResolve(customer, project,
                out var emsCompanyId, out var emsApiKey, out var emsBaseUrl, out _))
        {
            return Result<EmsImpersonationDto>.Failure(
                "Bu müşteri EMS/Liftdesk kaynaklı değil. Destek oturumu yalnızca Liftdesk kullanıcıları için açılabilir.");
        }

        EmsImpersonationResponse ems;
        try
        {
            ems = await _saasAClient.CreateImpersonationAsync(
                emsApiKey, emsCompanyId, request.UserId, agent, reason, cancellationToken, emsBaseUrl);
        }
        catch (EmsApiErrorException ex)
        {
            // errorCode varsa ona göre, yoksa sunucunun mesajı (sözleşme §2.5).
            var msg = ex.ErrorCode switch
            {
                "IMPERSONATION_USER_INACTIVE"      => "Kullanıcı pasif — destek oturumu açılamaz.",
                "IMPERSONATION_TARGET_NOT_ALLOWED" => "Hedef bir platform yöneticisi — destek kanalından bu hesaba oturum açılamaz.",
                _ => ex.StatusCode switch
                {
                    404 => "Kullanıcı bu firmada bulunamadı ya da silinmiş.",
                    401 => "Liftdesk API anahtarı geçersiz (401).",
                    503 => "Liftdesk tarafında CRM anahtarı tanımlı değil (503).",
                    _   => ex.ApiMessage,
                },
            };
            _logger.LogWarning(
                "EMS impersonation rejected for customer {CustomerId} (company {EmsId}, user {UserId}): {Status} {Code}",
                customer.Id, emsCompanyId, request.UserId, ex.StatusCode, ex.ErrorCode ?? "-");
            return Result<EmsImpersonationDto>.Failure(msg);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("EMS circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<EmsImpersonationDto>.Failure(
                "Liftdesk API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EMS impersonation failed for customer {CustomerId} (company {EmsId}, user {UserId}).",
                customer.Id, emsCompanyId, request.UserId);
            return Result<EmsImpersonationDto>.Failure($"Destek oturumu açılamadı: {ex.Message}");
        }

        // CRM tarafı denetim satırı — bağlantı YOK, gerekçe YOK (ikisi de Liftdesk'in denetim
        // kaydında; bağlantı gizli değer, gerekçe oraya ait). Yalnız kim/kime/ne zaman.
        _logger.LogInformation(
            "Impersonation issued: agent {Agent} -> customer {CustomerId} (company {EmsId}, user {UserId}), expires {ExpiresAt:u}.",
            agent, customer.Id, emsCompanyId, request.UserId, ems.ExpiresAt);

        return Result<EmsImpersonationDto>.Success(new EmsImpersonationDto(ems.LoginUrl, ems.ExpiresAt));
    }
}
