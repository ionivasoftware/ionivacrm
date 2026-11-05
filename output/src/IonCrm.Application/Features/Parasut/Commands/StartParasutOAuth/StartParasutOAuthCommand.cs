using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.StartParasutOAuth;

/// <summary>
/// Starts Paraşüt's authorization-code flow for the GLOBAL connection: generates <c>state</c> + PKCE verifier, stores their
/// hash / encrypted form on the connection row and returns the Paraşüt login URL the browser must be sent to.
/// </summary>
public sealed record StartParasutOAuthCommand : IRequest<Result<StartParasutOAuthDto>>;

public sealed record StartParasutOAuthDto(string AuthorizationUrl, DateTime StateExpiresAt);
