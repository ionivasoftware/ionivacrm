namespace IonCrm.Application.Common.Helpers;

/// <summary>
/// Checklist-specific guards. The Liftdesk customer resolution and failure mapping that this feature
/// shares with the other per-project Liftdesk surfaces live in <see cref="LiftdeskCustomerHelper"/>.
/// </summary>
public static class LiftdeskChecklistHelper
{
    /// <summary>Maintenance (bakım) checklist kind, as used in Liftdesk URLs.</summary>
    public const string KindMaintenance = "maintenance";

    /// <summary>Fault (arıza) checklist kind, as used in Liftdesk URLs.</summary>
    public const string KindFault = "fault";

    /// <summary>Reset-only scope: the escalator maintenance list.</summary>
    public const string KindEscalator = "escalator";

    /// <summary>Reset-only scope covering all lists.</summary>
    public const string KindBoth = "both";

    /// <summary>
    /// Returns true when <paramref name="kind"/> is a valid checklist kind. The read/replace endpoints
    /// only accept maintenance|fault (the escalator list is a maintenance list selected via <c>type</c>);
    /// reset additionally accepts "escalator" and "both" as scopes.
    /// </summary>
    public static bool IsValidKind(string? kind, bool allowResetScopes = false)
        => kind is KindMaintenance or KindFault
           || (allowResetScopes && kind is KindEscalator or KindBoth);
}
