using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// One lifecycle e-mail sent to a (non-TR) demo customer from the company support mailbox: welcome on
/// signup, "how is it going" mid-trial, and a reminder after the trial ended without a purchase. One row
/// per (customer, step) — the scheduler never repeats a step. Global table (customer is tenant-scoped).
/// </summary>
public class CustomerLifecycleMail : BaseEntity
{
    public Guid CustomerId { get; set; }

    public LifecycleStep Step { get; set; }

    /// <summary>Template language actually used (en, de…), derived from the customer's country.</summary>
    public string Language { get; set; } = "en";

    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public string? MessageId { get; set; }

    public LifecycleMailStatus Status { get; set; } = LifecycleMailStatus.Sent;

    public string? Error { get; set; }

    /// <summary>"auto" (scheduler) or the e-mail of the user who pressed "send now".</summary>
    public string TriggeredBy { get; set; } = "auto";
}
