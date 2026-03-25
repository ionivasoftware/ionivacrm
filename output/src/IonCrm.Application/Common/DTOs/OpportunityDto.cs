using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

/// <summary>Opportunity data transfer object.</summary>
public class OpportunityDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public OpportunityStage Stage { get; set; }
    public int? Probability { get; set; }
    public DateOnly? ExpectedCloseDate { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
