using System.Text;
using IonCrm.Infrastructure.ExternalApis.EmailCollector;
using MimeKit;

namespace IonCrm.Tests.VendorInvoices;

/// <summary>
/// Tests for <see cref="InvoiceEmailParser"/> — the part that has to see through forwards.
///
/// Real case behind these: a Google Workspace (ioniva.com) invoice forwarded from a personal address
/// into the accounting mailbox was never collected. Google puts the amount only in the attached PDF,
/// and a forward-as-attachment buries both the vendor headers and that PDF inside a nested
/// message/rfc822 part, which <c>MimeMessage.Attachments</c> does not descend into.
/// </summary>
public class InvoiceEmailParserTests
{
    private const string VendorAddress = "payments-noreply@google.com";
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 fake invoice");

    /// <summary>The vendor's own mail: HTML body + PDF attachment.</summary>
    private static MimeMessage BuildVendorInvoice()
    {
        var message = new MimeMessage
        {
            Subject = "Your Google Workspace invoice for ioniva.com",
            Date = new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero),
        };
        message.From.Add(new MailboxAddress("Google Payments", VendorAddress));
        message.To.Add(new MailboxAddress("Omer", "omer.cakmakci@ioniva.com"));

        var body = new TextPart("html") { Text = "<p>Invoice for <b>ioniva.com</b></p>" };
        var pdf = new MimePart("application", "pdf")
        {
            Content = new MimeContent(new MemoryStream(PdfBytes)),
            FileName = "invoice.pdf",
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
        };
        message.Body = new Multipart("mixed") { body, pdf };
        return message;
    }

    /// <summary>Gmail's "Forward as attachment": the original mail becomes a message/rfc822 part.</summary>
    private static MimeMessage BuildForwardedAsAttachment()
    {
        var forward = new MimeMessage
        {
            Subject = "Fwd: Your Google Workspace invoice for ioniva.com",
            Date = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
        };
        forward.From.Add(new MailboxAddress("Omer Cakmakci", "omer.cakmakci@ioniva.com"));
        forward.To.Add(new MailboxAddress("Muhasebe", "muhasebe@ioniva.com"));

        forward.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Ekte fatura var." },
            new MessagePart { Message = BuildVendorInvoice() },
        };
        return forward;
    }

    /// <summary>Inline forward: attachment stays top-level, original headers are quoted in the body.</summary>
    private static MimeMessage BuildForwardedInline()
    {
        var forward = new MimeMessage
        {
            Subject = "Fwd: Your Google Workspace invoice for ioniva.com",
            Date = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
        };
        forward.From.Add(new MailboxAddress("Omer Cakmakci", "omer.cakmakci@ioniva.com"));

        var pdf = new MimePart("application", "pdf")
        {
            Content = new MimeContent(new MemoryStream(PdfBytes)),
            FileName = "invoice.pdf",
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
        };
        forward.Body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = $"---------- Forwarded message ---------\nFrom: Google Payments <{VendorAddress}>" },
            pdf,
        };
        return forward;
    }

    // ── The regression: forward as attachment ─────────────────────────────────

    [Fact]
    public void ForwardedAsAttachment_FindsVendorSenderInsideNestedMessage()
    {
        var parsed = InvoiceEmailParser.Parse(BuildForwardedAsAttachment());

        // FromContains is matched against this; the outer From is only the colleague who forwarded it.
        parsed.From.Should().Contain(VendorAddress);
        parsed.From.Should().Contain("omer.cakmakci@ioniva.com");
    }

    [Fact]
    public void ForwardedAsAttachment_FindsPdfInsideNestedMessage()
    {
        var parsed = InvoiceEmailParser.Parse(BuildForwardedAsAttachment());

        // Without descending into the embedded message this is null and the amount is never found,
        // because Google states it only in the PDF.
        parsed.PdfBytes.Should().NotBeNull();
        parsed.PdfBytes.Should().BeEquivalentTo(PdfBytes);
        parsed.PdfFileName.Should().Be("invoice.pdf");
    }

    [Fact]
    public void ForwardedAsAttachment_UsesOriginalInvoiceDateNotForwardDate()
    {
        var parsed = InvoiceEmailParser.Parse(BuildForwardedAsAttachment());

        // The forward happened in August; the invoice is July's. Billing period derives from this.
        parsed.Date.Should().Be(new DateTime(2026, 7, 3, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ForwardedAsAttachment_SubjectForMatchCoversNestedSubject()
    {
        var parsed = InvoiceEmailParser.Parse(BuildForwardedAsAttachment());

        parsed.SubjectForMatch.Should().Contain("ioniva.com");
        // Display subject stays the message's own.
        parsed.Subject.Should().StartWith("Fwd:");
    }

    [Fact]
    public void ForwardedAsAttachment_BodyIncludesNestedBody()
    {
        var parsed = InvoiceEmailParser.Parse(BuildForwardedAsAttachment());

        parsed.Body.Should().Contain("Ekte fatura var.");
        parsed.Body.Should().Contain("ioniva.com");   // from the nested HTML body, tags stripped
    }

    // ── Still works for the shapes that already worked ────────────────────────

    [Fact]
    public void InlineForward_StillFindsSenderAndPdf()
    {
        var parsed = InvoiceEmailParser.Parse(BuildForwardedInline());

        parsed.From.Should().Contain("omer.cakmakci@ioniva.com");
        parsed.Body.Should().Contain(VendorAddress);   // quoted header
        parsed.PdfBytes.Should().NotBeNull();
        // No embedded message → the forward's own date is all there is.
        parsed.Date.Should().Be(new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void DirectVendorMail_IsUnaffected()
    {
        var parsed = InvoiceEmailParser.Parse(BuildVendorInvoice());

        parsed.From.Should().Contain(VendorAddress);
        parsed.Subject.Should().Contain("ioniva.com");
        parsed.PdfBytes.Should().NotBeNull();
        parsed.Date.Should().Be(new DateTime(2026, 7, 3, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void MailWithoutPdf_ReturnsNullBytes()
    {
        var message = new MimeMessage { Subject = "no attachment" };
        message.From.Add(new MailboxAddress("X", "x@example.com"));
        message.Body = new TextPart("plain") { Text = "hello" };

        var parsed = InvoiceEmailParser.Parse(message);

        parsed.PdfBytes.Should().BeNull();
        parsed.PdfFileName.Should().BeNull();
    }

    [Fact]
    public void StripHtml_ReducesMarkupToText()
    {
        InvoiceEmailParser.StripHtml("<p>Tutar: <b>$25.00</b></p>").Should().Be("Tutar: $25.00");
        InvoiceEmailParser.StripHtml(null).Should().BeNull();
    }
}
