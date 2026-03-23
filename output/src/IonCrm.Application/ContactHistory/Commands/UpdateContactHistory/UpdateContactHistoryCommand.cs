using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.ContactHistory.Commands.UpdateContactHistory;

/// <summary>Command to update an existing contact history record.</summary>
public record UpdateContactHistoryCommand : IRequest<Result<ContactHistoryDto>>
{
    public Guid Id { get; init; }
    public ContactType Type { get; init; }
    public string? Subject { get; init; }
    public string? Content { get; init; }
    public string? Outcome { get; init; }
    public DateTime ContactedAt { get; init; }
}
