using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.ConvertLeadToCustomer;

/// <summary>
/// Command to convert a Lead customer to an Active customer.
/// This is the pipeline step: Potential → Customer.
/// Only customers with Status == Lead can be converted.
/// </summary>
public record ConvertLeadToCustomerCommand(Guid CustomerId) : IRequest<Result<CustomerDto>>;
