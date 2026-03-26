using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Auth.Commands.UpdateProfile;

/// <summary>Updates the currently authenticated user's display name and optionally password.</summary>
public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, Result>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUser;

    public UpdateProfileCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUser)
    {
        _userRepository  = userRepository;
        _passwordHasher  = passwordHasher;
        _currentUser     = currentUser;
    }

    public async Task<Result> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure("Kimlik doğrulaması gereklidir.");

        var user = await _userRepository.GetByIdAsync(_currentUser.UserId, cancellationToken);
        if (user is null)
            return Result.Failure("Kullanıcı bulunamadı.");

        user.FirstName = request.FirstName.Trim();
        user.LastName  = request.LastName.Trim();

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (request.NewPassword.Length < 8)
                return Result.Failure("Şifre en az 8 karakter olmalıdır.");
            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        }

        await _userRepository.UpdateAsync(user, cancellationToken);
        return Result.Success();
    }
}
