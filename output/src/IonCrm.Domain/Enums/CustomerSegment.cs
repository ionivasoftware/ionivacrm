namespace IonCrm.Domain.Enums;

/// <summary>
/// Segment classification is now project-specific and stored as a free string on the Customer entity.
/// This enum is kept for reference only and is no longer used in code.
/// EMS: "Asansör Firması"
/// Rezerval: "Tekil Restoran" | "Zincir Restoran" | "Cafe" | "Club & Beach" | "Otel" | "Spa"
/// </summary>
[Obsolete("Segment is now a free string on Customer. See ProjectSegments config in Application layer.")]
public enum CustomerSegment
{
    // Kept for historical reference only
}
