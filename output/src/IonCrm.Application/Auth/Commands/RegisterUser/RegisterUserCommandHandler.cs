using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Commands.RegisterUser;

/// <summary>
/// Handles <see cref="RegisterUserCommand"/> — creates a new User with a BCrypt password hash.
/// NEVER logs the plain-text password.
/// </summary>
public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<RegisterUserCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="RegisterUserCommandHandler"/>.</summary>
    public RegisterUserCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ILogger<RegisterUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<UserDto>> Handle(
        RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _userRepository.EmailExistsAsync(email, cancellationToken))
            return Result<UserDto>.Failure($"Email '{email}' is already registered.");

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsSuperAdmin = request.IsSuperAdmin,
            IsActive = true
        };

        await _userRepository.AddAsync(user, cancellationToken);

        _logger.LogInformation(
            "New user registered: {UserId} ({Email}), SuperAdmin={IsSuperAdmin}",
            user.Id, email, user.IsSuperAdmin);

        return Result<UserDto>.Success(UserMappingHelper.MapToDto(user));
    }
}
