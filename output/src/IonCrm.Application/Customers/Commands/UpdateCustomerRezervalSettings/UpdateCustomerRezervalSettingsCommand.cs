using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.UpdateCustomerRezervalSettings;

/// <summary>
/// Updates the Rezerval reservation settings for a Rezerval-sourced customer.
/// Proxies to PUT https://rezback.rezerval.com/v1/Crm/ReservationSetting.
/// Only non-null fields are forwarded; unset fields keep their existing values on Rezerval.
/// </summary>
public record UpdateCustomerRezervalSettingsCommand(
    Guid CustomerId,
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
    string? ReviewGoogleLink
) : IRequest<Result<string>>;
