using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerEmsUsers;

/// <summary>
/// Returns the EMS user list for the customer identified by <paramref name="CustomerId"/>.
/// The customer must be an EMS-sourced customer (LegacyId is numeric or "SAASA-{n}").
/// Returns 400 if the customer has no EMS mapping.
/// </summary>
public record GetCustomerEmsUsersQuery(Guid CustomerId) : IRequest<Result<List<EmsCompanyUserDto>>>;

/// <summary>DTO representing a single EMS company user.</summary>
public record EmsCompanyUserDto(
    int UserId,
    string Name,
    string Surname,
    string Email,
    string Role,
    string LoginName,
    string Password);
