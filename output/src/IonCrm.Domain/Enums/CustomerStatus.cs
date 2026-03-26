namespace IonCrm.Domain.Enums;

/// <summary>Lifecycle status of a customer record.</summary>
public enum CustomerStatus
{
    /// <summary>Potential customer — not yet converted.</summary>
    Lead = 1,

    /// <summary>Active paying customer. Can only be set by SaaS sync, not by CRM users.</summary>
    Active = 2,

    /// <summary>Customer is in demo/trial phase.</summary>
    Demo = 3,

    /// <summary>Customer has churned / ended relationship.</summary>
    Churned = 4
}
