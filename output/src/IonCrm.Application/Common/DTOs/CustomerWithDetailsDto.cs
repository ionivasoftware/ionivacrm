using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

/// <summary>
/// Extended customer DTO including recent contact histories and open tasks.
/// Used by the GET /api/v1/customers/{id}/details endpoint.
/// </summary>
public class CustomerWithDetailsDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string? Code { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public string? TaxUnit { get; set; }
    public CustomerStatus Status { get; set; }
    public string? Segment { get; set; }
    public CustomerLabel? Label { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Total number of contact history records (not soft-deleted).</summary>
    public int TotalContactHistories { get; set; }

    /// <summary>Total number of tasks (not soft-deleted).</summary>
    public int TotalTasks { get; set; }

    /// <summary>Number of open (Todo or InProgress) tasks.</summary>
    public int OpenTasksCount { get; set; }

    /// <summary>The 5 most recent contact history entries, ordered by ContactedAt descending.</summary>
    public List<ContactHistoryDto> RecentContactHistories { get; set; } = new();

    /// <summary>All open (Todo or InProgress) tasks for this customer.</summary>
    public List<CustomerTaskDto> OpenTasks { get; set; } = new();
}
