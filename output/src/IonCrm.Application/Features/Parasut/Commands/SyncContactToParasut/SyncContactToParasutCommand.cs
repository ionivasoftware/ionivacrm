using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Commands.SyncContactToParasut;

/// <summary>
/// Pushes a CRM customer to Paraşüt as a contact (cari).
/// Creates the contact if it does not exist in Paraşüt; updates if it does.
/// </summary>
/// <param name="ProjectId">The project (tenant) whose Paraşüt connection will be used.</param>
/// <param name="CustomerId">The CRM customer to sync.</param>
public record SyncContactToParasutCommand(Guid ProjectId, Guid CustomerId)
    : IRequest<Result<SyncContactToParasutDto>>;

/// <summary>Response DTO after syncing a contact.</summary>
public record SyncContactToParasutDto(
    Guid CustomerId,
    string ParasutContactId,
    string ParasutContactName,
    bool WasCreated           // true = new contact created, false = existing updated
);
