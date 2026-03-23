namespace IonCrm.Domain.Enums;

/// <summary>Communication channel used in a contact history record.</summary>
public enum ContactType
{
    /// <summary>Phone call.</summary>
    Call = 1,

    /// <summary>Email communication.</summary>
    Email = 2,

    /// <summary>In-person or virtual meeting.</summary>
    Meeting = 3,

    /// <summary>Internal note — no direct customer contact.</summary>
    Note = 4,

    /// <summary>WhatsApp message.</summary>
    WhatsApp = 5,

    /// <summary>On-site visit.</summary>
    Visit = 6
}
