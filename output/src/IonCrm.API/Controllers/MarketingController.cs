using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Kullanıcının kendi Gmail'i ile gönderim: ayarlar (adres + uygulama şifresi, şifreli saklanır) ve
/// müşteriye tanıtım maili (docs/marketing şablonu + katalog + fiyat listesi, kendine BCC). Her gönderim
/// müşteri zaman çizelgesine E-posta kaydı olarak düşer.
/// </summary>
[Route("api/v1/marketing")]
[Authorize]
public sealed class MarketingController : ApiControllerBase
{
    private readonly IMarketingMailService _mail;
    private readonly IUserRepository _users;
    private readonly ICustomerRepository _customers;
    private readonly IContactHistoryRepository _contacts;
    private readonly ITokenProtector _protector;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<MarketingController> _logger;

    public MarketingController(IMarketingMailService mail, IUserRepository users, ICustomerRepository customers, IContactHistoryRepository contacts,
        ITokenProtector protector, ICurrentUserService currentUser, ILogger<MarketingController> logger)
    {
        _mail = mail; _users = users; _customers = customers; _contacts = contacts; _protector = protector; _currentUser = currentUser; _logger = logger;
    }

    // ── Kullanıcı e-posta ayarları ───────────────────────────────────────────

    /// <summary>GET /api/v1/marketing/mail-settings — adres, gönderen adı, BCC, şifre kayıtlı mı (şifre dönmez).</summary>
    [HttpGet("mail-settings")]
    public async Task<IActionResult> GetMailSettings(CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId, cancellationToken);
        if (user is null) return NotFound(ApiResponse<object>.Fail("Kullanıcı bulunamadı.", 404));
        return OkResponse(new MailSettingsDto(user.MailAddress, user.MailSenderName, user.MailBccSelf, !string.IsNullOrWhiteSpace(user.MailPasswordEncrypted),
            !string.IsNullOrWhiteSpace(user.MailAddress) && !string.IsNullOrWhiteSpace(user.MailPasswordEncrypted)));
    }

    /// <summary>PUT /api/v1/marketing/mail-settings — {address, password?, senderName?, bccSelf}. Şifre boşsa mevcut korunur.</summary>
    [HttpPut("mail-settings")]
    public async Task<IActionResult> SaveMailSettings([FromBody] SaveMailSettingsBody body, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId, cancellationToken);
        if (user is null) return NotFound(ApiResponse<object>.Fail("Kullanıcı bulunamadı.", 404));
        var address = body.Address?.Trim();
        if (string.IsNullOrWhiteSpace(address) || !MimeKit.MailboxAddress.TryParse(address, out _))
            return BadRequest(ApiResponse<object>.Fail("Geçerli bir e-posta adresi girin.", 400));
        if (!address.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) && !address.EndsWith("@googlemail.com", StringComparison.OrdinalIgnoreCase) && !body.AllowNonGmail)
            return BadRequest(ApiResponse<object>.Fail("Gönderim Gmail SMTP üzerinden yapılır; Google Workspace alan adı kullanıyorsanız 'Workspace adresi' seçeneğini işaretleyin.", 400));
        var password = body.Password?.Trim();
        if (string.IsNullOrEmpty(password) && string.IsNullOrWhiteSpace(user.MailPasswordEncrypted))
            return BadRequest(ApiResponse<object>.Fail("Uygulama şifresi gerekli (Google Hesabı → Güvenlik → Uygulama şifreleri, 16 hane).", 400));

        user.MailAddress = address;
        user.MailSenderName = string.IsNullOrWhiteSpace(body.SenderName) ? null : body.SenderName.Trim()[..Math.Min(body.SenderName.Trim().Length, 120)];
        user.MailBccSelf = body.BccSelf;
        if (!string.IsNullOrEmpty(password)) user.MailPasswordEncrypted = _protector.Protect(password.Replace(" ", string.Empty));
        await _users.UpdateAsync(user, cancellationToken);
        _logger.LogInformation("Kullanıcı e-posta gönderim ayarı güncellendi: {UserId} ({Address})", user.Id, address);
        return OkResponse(new MailSettingsDto(user.MailAddress, user.MailSenderName, user.MailBccSelf, true, true));
    }

    /// <summary>DELETE /api/v1/marketing/mail-settings — adres ve şifreyi sil.</summary>
    [HttpDelete("mail-settings")]
    public async Task<IActionResult> ClearMailSettings(CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId, cancellationToken);
        if (user is null) return NotFound(ApiResponse<object>.Fail("Kullanıcı bulunamadı.", 404));
        user.MailAddress = null; user.MailPasswordEncrypted = null; user.MailSenderName = null;
        await _users.UpdateAsync(user, cancellationToken);
        return OkResponse(new MailSettingsDto(null, null, user.MailBccSelf, false, false));
    }

    /// <summary>POST /api/v1/marketing/mail-settings/test — kendine test maili.</summary>
    [HttpPost("mail-settings/test")]
    public async Task<IActionResult> TestMail(CancellationToken cancellationToken = default)
    {
        var result = await _mail.SendTestAsync(_currentUser.UserId, cancellationToken);
        return result.Success ? OkResponse(result) : BadRequest(ApiResponse<object>.Fail(result.Message, 400));
    }

    // ── Materyaller + gönderim ───────────────────────────────────────────────

    /// <summary>GET /api/v1/marketing/materials — şablonlar (konu seçenekleriyle) ve ekler.</summary>
    [HttpGet("materials")]
    public IActionResult Materials()
    {
        var m = _mail.GetMaterials();
        return OkResponse(new { templates = m.Templates, attachments = m.Attachments });
    }

    /// <summary>
    /// POST /api/v1/marketing/customers/{customerId}/intro-email — {templateKey?, subjectIndex?, note?, to?}.
    /// Kullanıcının Gmail'inden gönderir, kendine BCC, zaman çizelgesine E-posta kaydı düşer.
    /// </summary>
    [HttpPost("customers/{customerId:guid}/intro-email")]
    public async Task<IActionResult> SendIntro(Guid customerId, [FromBody] SendIntroBody? body, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);
        if (customer is null) return NotFound(ApiResponse<object>.Fail("Müşteri bulunamadı.", 404));
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId)) return ForbiddenResponse<object>("Bu müşteriye erişim yok.");
        if (body?.Note is { Length: > 2000 }) return BadRequest(ApiResponse<object>.Fail("Not en fazla 2000 karakter.", 400));

        var result = await _mail.SendIntroAsync(_currentUser.UserId, customerId, body?.TemplateKey, body?.SubjectIndex ?? 0, body?.Note, body?.To, cancellationToken);
        if (!result.Success) return BadRequest(ApiResponse<object>.Fail(result.Message, 400));

        try
        {
            await _contacts.AddAsync(new Domain.Entities.ContactHistory
            {
                CustomerId = customer.Id, ProjectId = customer.ProjectId, Type = ContactType.Email,
                Subject = $"Tanıtım maili: {result.Subject}",
                Content = $"Alıcı: {result.To}\nEkler: {string.Join(", ", result.Attachments ?? new())}{(string.IsNullOrWhiteSpace(body?.Note) ? "" : $"\n\nKişisel not:\n{body!.Note!.Trim()}")}",
                Outcome = "Tanıtım maili gönderildi (katalog + fiyat listesi ekli)", ContactedAt = DateTime.UtcNow,
                CreatedByUserId = _currentUser.UserId == Guid.Empty ? null : _currentUser.UserId, Purpose = AppointmentKind.NewCall, Reached = true,
            }, cancellationToken);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Tanıtım maili görüşme kaydı yazılamadı"); }

        return OkResponse(result);
    }
}

public record MailSettingsDto(string? Address, string? SenderName, bool BccSelf, bool HasPassword, bool Configured);

public record SaveMailSettingsBody(string? Address, string? Password, string? SenderName, bool BccSelf = true, bool AllowNonGmail = false);

public record SendIntroBody(string? TemplateKey, int? SubjectIndex, string? Note, string? To);
