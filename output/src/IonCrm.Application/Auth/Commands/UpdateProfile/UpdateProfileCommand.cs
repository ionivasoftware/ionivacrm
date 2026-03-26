using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.UpdateProfile;

/// <summary>Updates the current user's profile (name and optionally password).</summary>
public record UpdateProfileCommand : IRequest<Result>
{
    public string FirstName    { get; init; } = string.Empty;
    public string LastName     { get; init; } = string.Empty;
    public string? NewPassword { get; init; }
}
