namespace IonCrm.Domain.Enums;

/// <summary>
/// Lifecycle status of a customer subscription contract.
/// </summary>
public enum ContractStatus
{
    /// <summary>Contract is currently active and (for EFT) generating monthly invoices.</summary>
    Active = 0,

    /// <summary>Contract has reached its end date or was superseded by a renewal.</summary>
    Completed = 1,

    /// <summary>Contract was manually cancelled before its natural end.</summary>
    Cancelled = 2
}
