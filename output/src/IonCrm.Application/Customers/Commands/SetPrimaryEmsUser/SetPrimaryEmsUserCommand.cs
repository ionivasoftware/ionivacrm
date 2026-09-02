using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.SetPrimaryEmsUser;

/// <summary>
/// Sets a firm's primary admin (the "ana kullanıcı admini") to one of its existing users. SuperAdmin
/// only. The change is delegated to the source (EMS/Liftdesk), which flips <c>User.IsOwner</c> from the
/// current owner to <see cref="UserId"/> in one transaction — leaving the previous primary as a normal
/// admin. The CRM stores no firm-user rows, so there is no local state to mutate.
/// </summary>
public record SetPrimaryEmsUserCommand(
    Guid CustomerId,
    string UserId)
    : IRequest<Result<SetPrimaryEmsUserDto>>;

/// <summary>Result after a successful primary-admin change.</summary>
public record SetPrimaryEmsUserDto(
    int CompanyId,
    string UserId,
    string? PreviousUserId);
