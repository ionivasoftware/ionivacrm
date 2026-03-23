namespace IonCrm.Domain.Enums;

/// <summary>Lifecycle status of a customer record.</summary>
public enum CustomerStatus
{
    /// <summary>Potential customer — not yet converted.</summary>
    Lead = 1,

    /// <summary>Active paying or engaged customer.</summary>
    Active = 2,

    /// <summary>Customer is currently inactive but not lost.</summary>
    Inactive = 3,

    /// <summary>Customer has churned / ended relationship.</summary>
    Churned = 4
}
