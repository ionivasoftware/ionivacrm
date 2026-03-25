using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.Customers.Commands.UpdateCustomer;

/// <summary>Command to update an existing customer.</summary>
public record UpdateCustomerCommand : IRequest<Result<CustomerDto>>
{
    public Guid Id { get; init; }
    public string? Code { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string? ContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? TaxNumber { get; init; }
    public string? TaxUnit { get; init; }
    public CustomerStatus Status { get; init; }
    public CustomerSegment? Segment { get; init; }
    public CustomerLabel? Label { get; init; }
    public Guid? AssignedUserId { get; init; }
}
