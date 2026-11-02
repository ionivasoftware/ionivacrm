using IonCrm.Domain.Entities;

namespace IonCrm.Application.ContactHistory;

/// <summary>
/// Keeps the customer card in step with what the latest contact record said about rival vendors:
/// "works with another company" sets the flag; a named competitor replaces the previous one; a record
/// that names none keeps the old name. Records that do not mention a competitor change nothing (the
/// topic simply did not come up).
/// </summary>
public static class CompetitorMirror
{
    /// <returns>true when the customer changed and must be saved.</returns>
    public static bool Apply(Customer customer, Domain.Entities.ContactHistory record)
    {
        if (!record.UsesCompetitor) return false;
        var changed = false;
        if (!customer.UsesCompetitor) { customer.UsesCompetitor = true; changed = true; }
        if (record.CompetitorId is { } id && customer.CompetitorId != id) { customer.CompetitorId = id; changed = true; }
        if (changed) customer.UpdatedAt = DateTime.UtcNow;
        return changed;
    }
}
