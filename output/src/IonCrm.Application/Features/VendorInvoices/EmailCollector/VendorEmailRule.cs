namespace IonCrm.Application.Features.VendorInvoices.EmailCollector;

/// <summary>
/// A rule that recognises one vendor's invoice e-mail and extracts the figures from it.
/// Bound from configuration (<c>EmailCollector:Rules</c>) so it can be tuned to the actual e-mails
/// without a code change. A message matches when all of the non-empty <c>*Contains</c> filters match;
/// the first matching rule wins.
/// </summary>
public sealed class VendorEmailRule
{
    /// <summary>Target vendor key — must match a <see cref="KnownProviders"/> / VendorInvoice.Provider value.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Case-insensitive substring the sender (name or address) must contain, e.g. "stripe.com".</summary>
    public string? FromContains { get; set; }

    /// <summary>Optional case-insensitive substring the subject must contain, e.g. "Anthropic".</summary>
    public string? SubjectContains { get; set; }

    /// <summary>Optional case-insensitive substring the body must contain (further disambiguation).</summary>
    public string? BodyContains { get; set; }

    /// <summary>Regex whose first capture group is the amount (searched in subject then body), e.g. <c>\$([0-9.,]+)</c>.</summary>
    public string? AmountRegex { get; set; }

    /// <summary>Currency for this vendor's invoices (default USD).</summary>
    public string? Currency { get; set; }

    /// <summary>Optional regex whose first capture group is the invoice number.</summary>
    public string? InvoiceNoRegex { get; set; }

    /// <summary>Optional regex whose first capture group is a link to the invoice PDF.</summary>
    public string? PdfUrlRegex { get; set; }

    /// <summary>
    /// Months to subtract from the e-mail's date to derive the billing period, since invoices arrive
    /// in arrears (e.g. a June invoice delivered in early July with offset 1 → period June). Default 1.
    /// </summary>
    public int PeriodMonthOffset { get; set; } = 1;
}
