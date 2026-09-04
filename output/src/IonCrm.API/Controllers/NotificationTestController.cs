using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Bildirim e-postası yapılandırmasını doğrulama ucu.
///
/// Neden var: SMTP ayarları girildikten sonra doğru çalışıp çalışmadığını anlamak için gerçek bir
/// olayı (ödeme gelmesi, yedeğin bozulması) beklemek gerekirdi — yani en kötü anda öğrenilirdi.
/// SuperAdmin'e özel, tek iş yapar: test postası gönderir.
/// </summary>
[Route("api/v1/notifications")]
[Authorize(Policy = "SuperAdmin")]
public sealed class NotificationTestController : ApiControllerBase
{
    private readonly INotificationEmailSender _mail;
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initialises a new instance of <see cref="NotificationTestController"/>.</summary>
    public NotificationTestController(INotificationEmailSender mail, ICurrentUserService currentUser)
    {
        _mail = mail;
        _currentUser = currentUser;
    }

    /// <summary>
    /// POST /api/v1/notifications/test — yapılandırılmış alıcılara test e-postası gönderir.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken = default)
    {
        if (!_mail.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail(
                "Bildirim e-postası yapılandırılmamış (Notifications:Smtp:Host ve Notifications:Recipients gerekli).", 400));

        var sent = await _mail.SendAsync(
            "ION CRM bildirim testi",
            "<p>Bu bir test e-postasıdır — bildirim yapılandırması çalışıyor.</p>" +
            $"<p>Gönderen kullanıcı: {System.Net.WebUtility.HtmlEncode(_currentUser.Email)}<br/>" +
            $"Zaman (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</p>",
            cancellationToken);

        return sent
            ? OkResponse<object>(new { sent = true })
            : BadRequest(ApiResponse<object>.Fail(
                "E-posta gönderilemedi — sunucu loglarındaki SMTP hatasına bakın.", 400));
    }
}
