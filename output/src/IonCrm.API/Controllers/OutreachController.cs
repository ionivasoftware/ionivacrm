using System.Net;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>Public unsubscribe endpoint for outreach e-mails (link in every message + List-Unsubscribe header). No session; token-only.</summary>
[Route("api/v1/outreach")]
[AllowAnonymous]
public sealed class OutreachController : ControllerBase
{
    private readonly IGlobalLeadRepository _repo;
    private readonly ILogger<OutreachController> _logger;

    public OutreachController(IGlobalLeadRepository repo, ILogger<OutreachController> logger) { _repo = repo; _logger = logger; }

    [HttpGet("unsubscribe/{token}")]
    [HttpPost("unsubscribe/{token}")]
    public async Task<IActionResult> Unsubscribe(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return Page("Invalid link", "This unsubscribe link is not valid.", 400);
        var lead = await _repo.GetByUnsubscribeTokenAsync(token.Trim(), cancellationToken);
        if (lead is null) return Page("Invalid link", "This unsubscribe link is not valid or has already been used.", 404);
        if (lead.OutreachStatus != OutreachStatus.Unsubscribed)
        {
            lead.OutreachStatus = OutreachStatus.Unsubscribed; lead.UnsubscribedAt = DateTime.UtcNow; lead.NextSendAt = null;
            await _repo.UpdateAsync(lead, cancellationToken);
            _logger.LogInformation("Outreach unsubscribe: {Company} ({Email})", lead.CompanyName, lead.Email);
        }
        return Page("You're unsubscribed", $"{WebUtility.HtmlEncode(lead.Email ?? "This address")} will not receive further messages from us.", 200);
    }

    private ContentResult Page(string title, string text, int status) => new()
    {
        StatusCode = status, ContentType = "text/html; charset=utf-8",
        Content = $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/><title>{WebUtility.HtmlEncode(title)}</title></head>" +
                  "<body style=\"margin:0;background:#F4F5F7;font-family:Arial,Helvetica,sans-serif;color:#0F1F33;\"><div style=\"max-width:520px;margin:60px auto;background:#fff;border:1px solid #E5E9EF;padding:32px;\">" +
                  $"<h1 style=\"font-size:20px;margin:0 0 12px;\">{WebUtility.HtmlEncode(title)}</h1><p style=\"font-size:14px;line-height:22px;color:#5C6B80;margin:0;\">{text}</p>" +
                  "<p style=\"font-size:12px;color:#8C99A9;margin:24px 0 0;\">LiftDesk by IONIVA</p></div></body></html>",
    };
}
