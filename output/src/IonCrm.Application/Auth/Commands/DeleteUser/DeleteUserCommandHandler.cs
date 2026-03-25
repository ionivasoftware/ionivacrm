using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Auth.Commands.DeleteUser;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Result>
{
    private readonly IUserRepository _userRepo;

    public DeleteUserCommandHandler(IUserRepository userRepo) => _userRepo = userRepo;

    public async Task<Result> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(request.Id, cancellationToken);
        if (user is null)
            return Result.Failure("User not found.");

        await _userRepo.DeleteAsync(user, cancellationToken);
        return Result.Success();
    }
}
