namespace IonCrm.Domain.Enums;

/// <summary>
/// Sales pipeline stages for opportunities.
/// Integer values are persisted in the database — do NOT change them.
/// Old values: Prospecting=1, Qualification=2, Proposal=3, Negotiation=4, ClosedWon=5, ClosedLost=6.
/// Migration: records with Stage=4 (Negotiation) are converted to Stage=3 (Demo).
/// </summary>
public enum OpportunityStage
{
    /// <summary>Initial outreach / new call.</summary>
    YeniArama = 1,

    /// <summary>Qualified potential customer.</summary>
    Potansiyel = 2,

    /// <summary>Demo scheduled or completed.</summary>
    Demo = 3,

    /// <summary>Customer won / active customer.</summary>
    Musteri = 5,

    /// <summary>Opportunity lost.</summary>
    Kayip = 6,
}
