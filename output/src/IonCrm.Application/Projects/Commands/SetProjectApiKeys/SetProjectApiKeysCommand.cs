using IonCrm.Application.Common.Models;
using IonCrm.Application.Projects.Commands.CreateProject;
using MediatR;

namespace IonCrm.Application.Projects.Commands.SetProjectApiKeys;

public record SetProjectApiKeysCommand(
    Guid Id,
    string? EmsBaseUrl,
    string? EmsApiKey,
    string? RezervAlBaseUrl,
    string? RezervAlApiKey) : IRequest<Result<ProjectDto>>;
