using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

/// <summary>Contact history data transfer object.</summary>
public class ContactHistoryDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProjectId { get; set; }
    public ContactType Type { get; set; }
    public string? Subject { get; set; }
    public string? Content { get; set; }
    public string? Outcome { get; set; }
    public DateTime ContactedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
