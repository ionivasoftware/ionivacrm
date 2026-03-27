using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.ConnectParasut;

/// <summary>
/// Connects a project to Paraşüt by authenticating with OAuth 2.0 password grant
/// and persisting the access/refresh tokens to the database.
/// </summary>
/// <param name="ProjectId">The CRM project (tenant) to link to Paraşüt.</param>
/// <param name="CompanyId">The numeric Paraşüt company (firma) ID.</param>
/// <param name="ClientId">OAuth client ID provided by Paraşüt.</param>
/// <param name="ClientSecret">OAuth client secret provided by Paraşüt.</param>
/// <param name="Username">Paraşüt account e-mail address.</param>
/// <param name="Password">Paraşüt account password.</param>
public record ConnectParasutCommand(
    Guid ProjectId,
    long CompanyId,
    string ClientId,
    string ClientSecret,
    string Username,
    string Password
) : IRequest<Result<ConnectParasutDto>>;

/// <summary>Response DTO for a successful Paraşüt connection.</summary>
public record ConnectParasutDto(
    Guid ProjectId,
    long CompanyId,
    string Username,
    bool IsConnected,
    DateTime TokenExpiresAt
);
