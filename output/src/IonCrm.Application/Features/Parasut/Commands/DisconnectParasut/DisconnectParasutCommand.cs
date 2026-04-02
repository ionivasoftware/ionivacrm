using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.DisconnectParasut;

/// <summary>
/// Removes a Paraşüt connection.
/// Pass <c>ProjectId = null</c> to disconnect the global connection.
/// Pass a specific <c>ProjectId</c> to disconnect only that project's connection.
/// </summary>
public record DisconnectParasutCommand(Guid? ProjectId) : IRequest<Result>;
