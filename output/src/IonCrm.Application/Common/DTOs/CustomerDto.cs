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
    /// <summary>Project-specific segment string (e.g. "Asansör Firması", "Tekil Restoran").</summary>
    public string? Segment { get; set; }
    public CustomerLabel? Label { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? LegacyId { get; set; }
    public string? ParasutContactId { get; set; }
    /// <summary>True if the customer is registered as an e-invoice (e-fatura) payer in Paraşüt.</summary>
    public bool IsEInvoicePayer { get; set; }
    /// <summary>E-invoice inbox address (PK / GB envelope identifier).</summary>
    public string? EInvoiceAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
