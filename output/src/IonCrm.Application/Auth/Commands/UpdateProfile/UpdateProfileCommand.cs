using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.UpdateProfile;

/// <summary>Updates the current user's profile (name, optionally password and theme preference).</summary>
public record UpdateProfileCommand : IRequest<Result>
{
    public string FirstName    { get; init; } = string.Empty;
    public string LastName     { get; init; } = string.Empty;
    public string? NewPassword { get; init; }
    /// <summary>UI theme preference: "dark" or "light". Null means no change.</summary>
    public string? ThemePreference { get; init; }
}
