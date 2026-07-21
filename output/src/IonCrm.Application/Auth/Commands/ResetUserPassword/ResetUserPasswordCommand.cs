using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.ResetUserPassword;

/// <summary>
/// Command for a SuperAdmin to reset another user's password. When <see cref="NewPassword"/> is null or
/// empty a strong random password is generated. Authorization is enforced in the API controller via
/// <c>[Authorize(Policy = "SuperAdmin")]</c>.
/// </summary>
public record ResetUserPasswordCommand(
    Guid UserId,
    string? NewPassword = null) : IRequest<Result<ResetUserPasswordResult>>;

/// <summary>The effective (possibly generated) password, shown once to the admin to hand over.</summary>
public record ResetUserPasswordResult(string Password);
