using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.DeleteUser;

public record DeleteUserCommand(Guid Id) : IRequest<Result>;
