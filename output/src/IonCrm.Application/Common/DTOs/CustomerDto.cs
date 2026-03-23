using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

/// <summary>Full customer data transfer object.</summary>
public class CustomerDto
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
    public CustomerSegment? Segment { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
