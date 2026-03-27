using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.DisconnectParasut;

/// <summary>Removes the Paraşüt connection for the given project.</summary>
public record DisconnectParasutCommand(Guid ProjectId) : IRequest<Result>;
