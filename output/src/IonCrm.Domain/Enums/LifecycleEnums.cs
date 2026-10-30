namespace IonCrm.Domain.Enums;

/// <summary>Demo lifecycle e-mail steps (order = chronological).</summary>
public enum LifecycleStep
{
    /// <summary>Right after the trial account is created.</summary>
    Welcome = 1,
    /// <summary>Mid-trial check-in ("how is it going?").</summary>
    CheckIn = 2,
    /// <summary>Trial ended without a purchase — reminder.</summary>
    TrialEnded = 3,
}

public enum LifecycleMailStatus
{
    Sent = 1,
    Failed = 2,
}
