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

    /// <summary>Reset-only scope covering both kinds.</summary>
    public const string KindBoth = "both";

    /// <summary>Returns true when <paramref name="kind"/> is a valid checklist kind.</summary>
    public static bool IsValidKind(string? kind, bool allowBoth = false)
        => kind is KindMaintenance or KindFault || (allowBoth && kind == KindBoth);
}
