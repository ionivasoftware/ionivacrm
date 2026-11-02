using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.CreateEmsImpersonation;

/// <summary>
/// "Hesaba Gir": destek operatörü için bir Liftdesk kullanıcısının hesabına parolasız, kısa süreli
/// ve Liftdesk tarafında denetim kayıtlı bir giriş bağlantısı alır (docs/crm-impersonation-api.md).
/// <c>agent</c> istemciden ALINMAZ — handler oturum açmış operatörün e-postasını koyar.
/// </summary>
/// <param name="AccessRequestId">Customer consent to log in under (optional). When given it must be approved and inside its window; the login is counted on it.</param>
public record CreateEmsImpersonationCommand(
    Guid CustomerId,
    string UserId,
    string Reason,
    Guid? AccessRequestId = null)
    : IRequest<Result<EmsImpersonationDto>>;

/// <summary>
/// Tek kullanımlık giriş bağlantısı. <see cref="LoginUrl"/> gizli değerdir: tarayıcı yalnız yeni
/// sekmede açar; CRM loglamaz, saklamaz, ekranda metin olarak göstermez.
/// </summary>
public record EmsImpersonationDto(
    string LoginUrl,
    DateTime ExpiresAt);
