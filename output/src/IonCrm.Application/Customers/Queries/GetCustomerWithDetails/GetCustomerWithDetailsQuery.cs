using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerWithDetails;

/// <summary>
/// Query to retrieve a customer with full navigation details:
/// recent contact histories and open tasks.
/// </summary>
public record GetCustomerWithDetailsQuery(Guid Id) : IRequest<Result<CustomerWithDetailsDto>>;
