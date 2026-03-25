using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Auth.Commands.UpdateUser;

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, Result<UserDto>>
{
    private readonly IUserRepository _userRepo;

    public UpdateUserCommandHandler(IUserRepository userRepo) => _userRepo = userRepo;

    public async Task<Result<UserDto>> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdWithRolesAsync(request.Id, cancellationToken);
        if (user is null)
            return Result<UserDto>.Failure("User not found.");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.IsActive = request.IsActive;
        user.IsSuperAdmin = request.IsSuperAdmin;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateAsync(user, cancellationToken);
        return Result<UserDto>.Success(Auth.UserMappingHelper.MapToDto(user));
    }
}
