using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Customers.Commands.CreateCustomer;

/// <summary>Command to create a new customer within a project (tenant).</summary>
public record CreateCustomerCommand : IRequest<Result<CustomerDto>>
{
    public Guid ProjectId { get; init; }
    public string? Code { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string? ContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? TaxNumber { get; init; }
    public string? TaxUnit { get; init; }
    public CustomerStatus Status { get; init; } = CustomerStatus.Lead;
    /// <summary>Project-specific segment string. Cannot be set to Active — only SaaS sync may do that.</summary>
    public string? Segment { get; init; }
    public CustomerLabel? Label { get; init; }
    public Guid? AssignedUserId { get; init; }
}
