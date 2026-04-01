using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Projects.Commands.AddSms;

/// <summary>
/// Adds SMS credits to a company (Project) by the specified count.
/// Any authenticated user who belongs to the project, or a SuperAdmin, may call this.
/// </summary>
public record AddSmsCommand(
    Guid CompanyId,
    int Count) : IRequest<Result<AddSmsDto>>;

/// <summary>Result returned after SMS credits are added successfully.</summary>
public record AddSmsDto(
    Guid CompanyId,
    int SmsCount,
    int Added);
