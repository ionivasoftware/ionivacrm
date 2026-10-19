namespace IonCrm.Application.Common.Interfaces;

public record MarketingTemplateInfo(string Key, string FileName, List<string> Subjects, string? Language);

public record MarketingAttachmentInfo(string FileName, long SizeBytes);

public record MarketingMaterials(List<MarketingTemplateInfo> Templates, List<MarketingAttachmentInfo> Attachments, string Folder);

public record MailSendResult(bool Success, string Message, string? To = null, string? Subject = null, List<string>? Attachments = null);

/// <summary>Sends the Liftdesk intro e-mail (template + catalogue + price list from docs/marketing) through the user's own Gmail.</summary>
public interface IMarketingMailService
{
    MarketingMaterials GetMaterials();

    /// <summary>Sends to the customer's e-mail (or <paramref name="toOverride"/>), BCC to the user when enabled; logs nothing itself.</summary>
    Task<MailSendResult> SendIntroAsync(Guid userId, Guid customerId, string? templateKey, int subjectIndex, string? note, string? toOverride, CancellationToken cancellationToken = default);

    /// <summary>Sends a short test message from the user's Gmail to the user's own address.</summary>
    Task<MailSendResult> SendTestAsync(Guid userId, CancellationToken cancellationToken = default);
}
