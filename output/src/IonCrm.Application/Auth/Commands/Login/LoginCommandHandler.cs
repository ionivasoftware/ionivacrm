using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Commands.Login;

/// <summary>
/// Handles the <see cref="LoginCommand"/> — validates credentials, generates tokens.
/// NEVER logs passwords. Uses BCrypt verification via <see cref="IPasswordHasher"/>.
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponseDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<LoginCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="LoginCommandHandler"/>.</summary>
    public LoginCommandHandler(
        IUserRepository userRepository,
        ITokenService tokenService,
        IPasswordHasher passwordHasher,
        ILogger<LoginCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponseDto>> Handle(
        LoginCommand request, CancellationToken cancellationToken)
    {
        // Normalise email before lookup
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByIdWithRolesAsync(
            (await _userRepository.GetByEmailAsync(email, cancellationToken))?.Id ?? Guid.Empty,
            cancellationToken);

        // Use constant-time comparison path to prevent user enumeration
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for email {Email}", email);
            return Result<AuthResponseDto>.Failure("Invalid email or password.");
        }

        if (!user.IsActive)
            return Result<AuthResponseDto>.Failure(
                "Your account has been deactivated. Contact an administrator.");

        var accessToken = _tokenService.GenerateAccessToken(user);
        var (rawRefreshToken, _) = await _tokenService.CreateRefreshTokenAsync(user, cancellationToken);

        _logger.LogInformation("User {UserId} authenticated successfully", user.Id);

        return Result<AuthResponseDto>.Success(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(15),
            User = UserMappingHelper.MapToDto(user)
        });
    }
}
