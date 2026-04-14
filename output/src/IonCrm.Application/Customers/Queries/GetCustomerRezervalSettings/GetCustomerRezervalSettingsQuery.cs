using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerRezervalSettings;

/// <summary>
/// Returns the Rezerval reservation settings (SMS texts, confirm/review cadence, flags) for a
/// Rezerval-sourced customer. The customer must have a LegacyId starting with "SAASB-" or "REZV-".
/// Proxies to GET https://rezback.rezerval.com/v1/Crm/ReservationSetting?companyId={id}.
/// </summary>
public record GetCustomerRezervalSettingsQuery(Guid CustomerId)
    : IRequest<Result<RezervalReservationSettingDto>>;

/// <summary>
/// DTO mirroring Rezerval's ReservationSetting payload. All fields are nullable because the
/// Rezerval response itself returns nulls for unconfigured options.
/// </summary>
public record RezervalReservationSettingDto(
    int? Id,
    int CompanyId,
    bool? IsAcceptWithoutPhone,
    bool? IsRequireConfirm,
    bool? IsSendConfirmSameDayReservations,
    bool? ConfirmSmsSetting,
    int? ConfirmSmsHour,
    bool? ReviewSmsSetting,
    int? ReviewSmsHour,
    int? PreparationTime,
    int? NotSendSmsMinHourId,
    int? NotSendSmsMaxHourId,
    bool? IsEnterAccountClosingInfo,
    bool? IsOtoTableAppoint,
    bool? IsSendReservationSms,
    bool? IsSendNotification,
    bool? IsSendReservationNotification,
    bool? IsSendCancelNotification,
    bool? IsSendConfirmNotification,
    bool? IsSendRegisterSms,
    int? IsSendRegisterMinute,
    string? SmsTextRegister,
    string? SmsTextConfirm,
    string? SmsTextReview,
    string? ReviewGoogleLink);
