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
    string UserId,
    string Name,
    string Surname,
    string Email,
    string Role,
    string LoginName,
    string Password,
    /// <summary>True when this user is the firm's primary admin (Liftdesk owner). Null on older
    /// Liftdesk builds that don't emit the flag yet — the UI then can't pre-badge the current primary.</summary>
    bool? IsPrimaryAdmin = null,
    /// <summary>True when the user is active in the source. Null on older builds.</summary>
    bool? IsActive = null);
