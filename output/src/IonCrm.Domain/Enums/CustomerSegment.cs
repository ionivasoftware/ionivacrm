namespace IonCrm.Domain.Enums;

/// <summary>Business segment classification of a customer.</summary>
public enum CustomerSegment
{
    /// <summary>Small and medium-sized enterprise.</summary>
    SME = 1,

    /// <summary>Large enterprise customer.</summary>
    Enterprise = 2,

    /// <summary>Early-stage startup.</summary>
    Startup = 3,

    /// <summary>Government or public sector entity.</summary>
    Government = 4,

    /// <summary>Individual / sole proprietor.</summary>
    Individual = 5
}
