namespace IonCrm.Application.Common.DTOs;

/// <summary>
/// Paraşüt product mapping DTO. Mappings are project-independent (global) — there is one
/// global catalog shared by all projects, mirroring the global Paraşüt connection.
/// </summary>
public class ParasutProductDto
{
    public Guid Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ParasutProductId { get; set; } = string.Empty;
    public string? ParasutProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public string? EmsProductId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
