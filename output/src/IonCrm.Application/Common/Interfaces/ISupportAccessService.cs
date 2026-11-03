using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Common.Interfaces;

public record SupportAccessRequestDto(Guid Id, Guid CustomerId, string EmsUserId, string? EmsUserName, string To, string Reason, string Status,
    string RequestedBy, string RequestedByName, DateTime RequestedAt, DateTime LinkExpiresAt, DateTime? RespondedAt, DateTime? ExpiresAt,
    int ValidHours, bool Active, bool Pending, int UseCount, string? SendError, string? Language = null);

/// <summary>
/// What the "Hesaba Gir" dialog needs: the approval in force (if any), the request still waiting (if any), recent history,
/// sender mailbox, and the language a new request mail would be written in (<see cref="MailLanguage"/> "tr"/"en") with
/// where that came from (<see cref="MailLanguageSource"/>: firm language / country / phone / default) so the operator can override it.
/// </summary>
public record SupportAccessOverview(SupportAccessRequestDto? Active, SupportAccessRequestDto? PendingRequest, List<SupportAccessRequestDto> Recent, string? SenderAddress, string? CustomerEmail,
    string MailLanguage = "tr", string MailLanguageSource = "varsayılan");

/// <summary>Customer consent before a support login: request by e-mail, answer through a tokenised link, time-boxed approval.</summary>
public interface ISupportAccessService
{
    Task<SupportAccessOverview> GetAsync(Guid customerId, string emsUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the request, cancels older pending ones for the same user, e-mails the customer. Fails (nothing stored) when the mail
    /// cannot be sent. <paramref name="language"/> ("tr"/"en") overrides the derived mail language; null = firm language → country → phone → Turkish.
    /// </summary>
    Task<Result<SupportAccessRequestDto>> RequestAsync(Guid customerId, string emsUserId, string? emsUserName, string to, string reason, int? validHours, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>Null when a support login may be opened under this request now, else the Turkish reason.</summary>
    Task<string?> ValidateForLoginAsync(Guid requestId, Guid customerId, string emsUserId, CancellationToken cancellationToken = default);

    /// <summary>Counts a support session opened under the approval.</summary>
    Task MarkUsedAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Request behind a link token (any tenant), or null.</summary>
    Task<SupportAccessRequest?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Applies the customer's answer; false when the link is dead (already answered, replaced, stale).</summary>
    Task<bool> RespondAsync(SupportAccessRequest request, bool approve, CancellationToken cancellationToken = default);

    /// <summary>Customer row for a request (any tenant) — company name, contact, country for the public pages.</summary>
    Task<Customer?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
}
