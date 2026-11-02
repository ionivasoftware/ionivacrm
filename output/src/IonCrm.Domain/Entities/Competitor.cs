using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// A rival vendor a lead or customer works with ("farklı firma ile çalışıyor"). Tenant-scoped like
/// customers: Liftdesk's competitors differ from RezervAl's. Created on the fly from the contact record
/// dialog when the name is not in the list; names are unique per project (case-insensitive).
/// </summary>
public class Competitor : BaseEntity
{
    /// <summary>Project (tenant) the competitor belongs to.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Display name as entered by the operator (trimmed).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional website.</summary>
    public string? Website { get; set; }

    /// <summary>Free notes: pricing, strengths, what customers say.</summary>
    public string? Notes { get; set; }

    public Project Project { get; set; } = null!;
}
