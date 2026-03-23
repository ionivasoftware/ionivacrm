using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using MediatR;

namespace IonCrm.Application.ContactHistory.Commands.CreateContactHistory;

/// <summary>Command to log a new customer interaction.</summary>
public record CreateContactHistoryCommand : IRequest<Result<ContactHistoryDto>>
{
    public Guid CustomerId { get; init; }
    public ContactType Type { get; init; }
    public string? Subject { get; init; }
    public string? Content { get; init; }
    public string? Outcome { get; init; }
    public DateTime ContactedAt { get; init; }
}
