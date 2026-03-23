using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerById;

/// <summary>Query to retrieve a customer by its ID.</summary>
public record GetCustomerByIdQuery(Guid Id) : IRequest<Result<CustomerDto>>;
