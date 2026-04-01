using IonCrm.Application.Common.DTOs;
using IonCrm.Domain.Entities;

namespace IonCrm.Application.Features.Invoices.Mappings;

/// <summary>Extension methods for mapping Invoice entities to DTOs.</summary>
public static class InvoiceMappings
{
    public static InvoiceDto ToDto(this Invoice i) => new()
    {
        Id = i.Id,
        ProjectId = i.ProjectId,
        CustomerId = i.CustomerId,
        CustomerName = i.Customer?.CompanyName ?? string.Empty,
        Title = i.Title,
        Description = i.Description,
        InvoiceSeries = i.InvoiceSeries,
        InvoiceNumber = i.InvoiceNumber,
        IssueDate = i.IssueDate,
        DueDate = i.DueDate,
        Currency = i.Currency,
        GrossTotal = i.GrossTotal,
        NetTotal = i.NetTotal,
        LinesJson = i.LinesJson,
        Status = i.Status,
        ParasutId = i.ParasutId,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt
    };
}
