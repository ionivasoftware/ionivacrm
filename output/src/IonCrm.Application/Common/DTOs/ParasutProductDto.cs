namespace IonCrm.Application.Common.DTOs;

/// <summary>Paraşüt product data transfer object.</summary>
public class ParasutProductDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ParasutProductId { get; set; } = string.Empty;
    public string? ParasutProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public string? EmsProductId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
