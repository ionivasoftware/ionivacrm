namespace IonCrm.Domain.Enums;

/// <summary>Sales pipeline stage for an opportunity.</summary>
public enum OpportunityStage
{
    Prospecting = 1,
    Qualification = 2,
    Proposal = 3,
    Negotiation = 4,
    ClosedWon = 5,
    ClosedLost = 6
}
