using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.UpdateCustomerRezervalSettings;

/// <summary>Handles <see cref="UpdateCustomerRezervalSettingsCommand"/>.</summary>
public sealed class UpdateCustomerRezervalSettingsCommandHandler
    : IRequestHandler<UpdateCustomerRezervalSettingsCommand, Result<string>>
{
    // Rezerval requires a numeric updatedBy on the PUT payload. We don't have a CRM→Rezerval
    // user mapping so we send a fixed system id — Rezerval accepts any numeric value here.
    private const int RezervalSystemUserId = 1;

    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasBClient _saasBClient;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateCustomerRezervalSettingsCommandHandler> _logger;

    public UpdateCustomerRezervalSettingsCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasBClient saasBClient,
        ICurrentUserService currentUser,
        ILogger<UpdateCustomerRezervalSettingsCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasBClient        = saasBClient;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    public async Task<Result<string>> Handle(
        UpdateCustomerRezervalSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<string>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<string>.Failure("Bu müşteriye erişim yetkiniz yok.");

        if (string.IsNullOrEmpty(customer.LegacyId)
            || (!customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
             && !customer.LegacyId.StartsWith("REZV-", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<string>.Failure(
                "Bu müşteri Rezerval kaynaklı değil. Rezerval ayarları yalnızca Rezerval müşterileri için güncellenebilir.");
        }

        string rawId = customer.LegacyId.StartsWith("SAASB-", StringComparison.OrdinalIgnoreCase)
            ? customer.LegacyId["SAASB-".Length..]
            : customer.LegacyId["REZV-".Length..];

        if (!int.TryParse(rawId, out var rezervalCompanyId))
            return Result<string>.Failure("Müşterinin Rezerval şirket numarası okunamadı.");

        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var rezervalApiKey = project?.RezervAlApiKey;
        if (string.IsNullOrWhiteSpace(rezervalApiKey))
            return Result<string>.Failure("Bu projede Rezerval API anahtarı yapılandırılmamış.");

        var payload = new RezervalReservationSettingUpdateRequest(
            CompanyId:                        rezervalCompanyId,
            UpdatedBy:                        RezervalSystemUserId,
            IsAcceptWithoutPhone:             request.IsAcceptWithoutPhone,
            IsRequireConfirm:                 request.IsRequireConfirm,
            IsSendConfirmSameDayReservations: request.IsSendConfirmSameDayReservations,
            ConfirmSmsSetting:                request.ConfirmSmsSetting,
            ConfirmSmsHour:                   request.ConfirmSmsHour,
            ReviewSmsSetting:                 request.ReviewSmsSetting,
            ReviewSmsHour:                    request.ReviewSmsHour,
            PreparationTime:                  request.PreparationTime,
            NotSendSmsMinHourId:              request.NotSendSmsMinHourId,
            NotSendSmsMaxHourId:              request.NotSendSmsMaxHourId,
            IsEnterAccountClosingInfo:        request.IsEnterAccountClosingInfo,
            IsOtoTableAppoint:                request.IsOtoTableAppoint,
            IsSendReservationSms:             request.IsSendReservationSms,
            IsSendNotification:               request.IsSendNotification,
            IsSendReservationNotification:    request.IsSendReservationNotification,
            IsSendCancelNotification:         request.IsSendCancelNotification,
            IsSendConfirmNotification:        request.IsSendConfirmNotification,
            IsSendRegisterSms:                request.IsSendRegisterSms,
            IsSendRegisterMinute:             request.IsSendRegisterMinute,
            SmsTextRegister:                  request.SmsTextRegister,
            SmsTextConfirm:                   request.SmsTextConfirm,
            SmsTextReview:                    request.SmsTextReview,
            ReviewGoogleLink:                 request.ReviewGoogleLink);

        try
        {
            var response = await _saasBClient.UpdateReservationSettingAsync(
                payload, rezervalApiKey, cancellationToken);

            _logger.LogInformation(
                "Updated Rezerval reservation setting for customer {CustomerId} (Rezerval company {RezvId}).",
                customer.Id, rezervalCompanyId);

            return Result<string>.Success(response.Message ?? "Rezervasyon ayarı güncellendi.");
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rezerval circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<string>.Failure(
                "Rezerval API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Rezerval UpdateReservationSetting failed for customer {CustomerId} (Rezerval company {RezvId}).",
                customer.Id, rezervalCompanyId);
            return Result<string>.Failure($"Rezerval rezervasyon ayarı güncellenemedi: {ex.Message}");
        }
    }
}
