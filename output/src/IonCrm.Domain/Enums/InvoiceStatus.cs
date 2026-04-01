namespace IonCrm.Domain.Enums;

/// <summary>
/// Lifecycle status of a CRM invoice.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Invoice is saved in CRM but not yet sent to Paraşüt.</summary>
    Draft = 0,

    /// <summary>Invoice has been transferred to Paraşüt (parasut_id is set).</summary>
    TransferredToParasut = 1,

    /// <summary>Invoice has been paid.</summary>
    Paid = 2,

    /// <summary>Invoice has been cancelled.</summary>
    Cancelled = 3,

    /// <summary>Invoice has been officialized via e-Invoice or e-Archive on Paraşüt.</summary>
    Officialized = 4
}
