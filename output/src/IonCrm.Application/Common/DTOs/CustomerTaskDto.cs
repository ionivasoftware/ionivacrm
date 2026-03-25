using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

/// <summary>Customer task data transfer object.</summary>
public class CustomerTaskDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskPriority Priority { get; set; }
    public IonCrm.Domain.Enums.TaskStatus Status { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
