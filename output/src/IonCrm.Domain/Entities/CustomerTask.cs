using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Represents a to-do task assigned to a customer.
/// Named CustomerTask to avoid conflict with System.Threading.Tasks.Task.
/// </summary>
public class CustomerTask : BaseEntity
{
    /// <summary>Gets or sets the customer this task is associated with.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the project (tenant) identifier (denormalized).</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Gets or sets the task title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets optional detailed description of the task.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the due date and time (UTC).</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Gets or sets the priority level of this task.</summary>
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    /// <summary>Gets or sets the current workflow status of this task.</summary>
    public Enums.TaskStatus Status { get; set; } = Enums.TaskStatus.Todo;

    /// <summary>Gets or sets the user this task is assigned to.</summary>
    public Guid? AssignedUserId { get; set; }

    // Navigation properties
    /// <summary>Gets or sets the associated customer.</summary>
    public Customer Customer { get; set; } = null!;

    /// <summary>Gets or sets the project (tenant).</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Gets or sets the assigned user.</summary>
    public User? AssignedUser { get; set; }
}
