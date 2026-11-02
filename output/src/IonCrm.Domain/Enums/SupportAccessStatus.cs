namespace IonCrm.Domain.Enums;

/// <summary>Lifecycle of a customer's consent for a support login ("Hesaba Gir" with permission).</summary>
public enum SupportAccessStatus
{
    /// <summary>Request e-mail sent, customer has not answered yet.</summary>
    Pending = 1,
    /// <summary>Customer approved — logins allowed until <c>ExpiresAt</c>.</summary>
    Approved = 2,
    /// <summary>Customer declined.</summary>
    Declined = 3,
    /// <summary>A newer request for the same user replaced this one before it was answered.</summary>
    Cancelled = 4,
}
