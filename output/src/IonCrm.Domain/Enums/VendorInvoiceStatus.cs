namespace IonCrm.Domain.Enums;

/// <summary>
/// Lifecycle of a vendor invoice reconciliation record.
///
/// Expected → Received → Reconciled   (amounts match: |expected-received| ≤ 0.01)
///                     → Mismatch     (amounts differ — accounting must review)
/// Expected → Missing                 (due date passed, no PDF received — ALARM)
/// </summary>
public enum VendorInvoiceStatus
{
    /// <summary>We know a bill is coming (from a cost API or a fixed subscription) but the PDF has not arrived.</summary>
    Expected = 1,

    /// <summary>The PDF invoice has been received but not yet reconciled against the expected amount.</summary>
    Received = 2,

    /// <summary>Received amount matches the expected amount within tolerance.</summary>
    Reconciled = 3,

    /// <summary>Received amount does not match the expected amount.</summary>
    Mismatch = 4,

    /// <summary>Due date passed with no invoice received — raised as an alarm.</summary>
    Missing = 5
}
