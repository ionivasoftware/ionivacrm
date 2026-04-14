using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetCustomerRezervalSettings;

/// <summary>Handles <see cref="GetCustomerRezervalSettingsQuery"/>.</summary>
public sealed class GetCustomerRezervalSettingsQueryHandler
    : IRequestHandler<GetCustomerRezervalSettingsQuery, Result<RezervalReservationSettingDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasBClient _saasBClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCustomerRezervalSettingsQueryHandler> _logger;

    public GetCustomerRezervalSettingsQueryHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasBClient saasBClient,
        ICurrentUserService currentUser,
        ILogger<GetCustomerRezervalSettingsQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasBClient        = saasBClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    public async Task<Result<RezervalReservationSettingDto>> Handle(
        GetCustomerRezervalSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<RezervalReservationSettingDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<RezervalReservationSettingDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        if (string.IsNullOrEmpty(customer.LegacyId)
            || (!customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
             && !customer.LegacyId.StartsWith("REZV-", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<RezervalReservationSettingDto>.Failure(
                "Bu müşteri Rezerval kaynaklı değil. Rezerval ayarları yalnızca Rezerval müşterileri için sorgulanabilir.");
        }

        string rawId = customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
            ? customer.LegacyId["SAASB-".Length..]
            : customer.LegacyId["REZV-".Length..];

        if (!int.TryParse(rawId, out var rezervalCompanyId))
            return Result<RezervalReservationSettingDto>.Failure("Müşterinin Rezerval şirket numarası okunamadı.");

        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var rezervalApiKey = project?.RezervAlApiKey;
        if (string.IsNullOrWhiteSpace(rezervalApiKey))
            return Result<RezervalReservationSettingDto>.Failure("Bu projede Rezerval API anahtarı yapılandırılmamış.");

        RezervalReservationSettingResponse response;
        try
        {
            response = await _saasBClient.GetReservationSettingAsync(
                rezervalCompanyId, rezervalApiKey, cancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rezerval circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<RezervalReservationSettingDto>.Failure(
                "Rezerval API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rezerval GetReservationSetting failed for customer {CustomerId} (Rezerval company {RezvId}).",
                customer.Id, rezervalCompanyId);
            return Result<RezervalReservationSettingDto>.Failure(
                $"Rezerval'dan rezervasyon ayarı alınamadı: {ex.Message}");
        }

        if (response.Data is null)
            return Result<RezervalReservationSettingDto>.Failure("Rezerval boş rezervasyon ayarı döndü.");

        var s = response.Data;
        var dto = new RezervalReservationSettingDto(
            Id:                               s.Id,
            CompanyId:                        s.CompanyId,
            IsAcceptWithoutPhone:             s.IsAcceptWithoutPhone,
            IsRequireConfirm:                 s.IsRequireConfirm,
            IsSendConfirmSameDayReservations: s.IsSendConfirmSameDayReservations,
            ConfirmSmsSetting:                s.ConfirmSmsSetting,
            ConfirmSmsHour:                   s.ConfirmSmsHour,
            ReviewSmsSetting:                 s.ReviewSmsSetting,
            ReviewSmsHour:                    s.ReviewSmsHour,
            PreparationTime:                  s.PreparationTime,
            NotSendSmsMinHourId:              s.NotSendSmsMinHourId,
            NotSendSmsMaxHourId:              s.NotSendSmsMaxHourId,
            IsEnterAccountClosingInfo:        s.IsEnterAccountClosingInfo,
            IsOtoTableAppoint:                s.IsOtoTableAppoint,
            IsSendReservationSms:             s.IsSendReservationSms,
            IsSendNotification:               s.IsSendNotification,
            IsSendReservationNotification:    s.IsSendReservationNotification,
            IsSendCancelNotification:         s.IsSendCancelNotification,
            IsSendConfirmNotification:        s.IsSendConfirmNotification,
            IsSendRegisterSms:                s.IsSendRegisterSms,
            IsSendRegisterMinute:             s.IsSendRegisterMinute,
            SmsTextRegister:                  s.SmsTextRegister,
            SmsTextConfirm:                   s.SmsTextConfirm,
            SmsTextReview:                    s.SmsTextReview,
            ReviewGoogleLink:                 s.ReviewGoogleLink);

        return Result<RezervalReservationSettingDto>.Success(dto);
    }
}
