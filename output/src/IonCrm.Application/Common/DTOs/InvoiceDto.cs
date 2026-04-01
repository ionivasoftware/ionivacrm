using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

/// <summary>Invoice data transfer object.</summary>
public class InvoiceDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? InvoiceSeries { get; set; }
    public int? InvoiceNumber { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Currency { get; set; } = "TRL";
    public decimal GrossTotal { get; set; }
    public decimal NetTotal { get; set; }
    public string LinesJson { get; set; } = "[]";
    public InvoiceStatus Status { get; set; }
    public string? ParasutId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
