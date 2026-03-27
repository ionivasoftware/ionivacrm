using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.LinkParasutContact;

/// <summary>Links an existing Paraşüt contact to a CRM customer.</summary>
public record LinkParasutContactCommand(
    Guid CustomerId,
    Guid ProjectId,
    string ParasutContactId
) : IRequest<Result<LinkParasutContactDto>>;

public record LinkParasutContactDto(
    Guid CustomerId,
    string ParasutContactId,
    string ParasutContactName
);
