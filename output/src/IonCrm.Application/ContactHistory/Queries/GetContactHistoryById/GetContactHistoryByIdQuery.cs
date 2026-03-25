using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetContactHistoryById;

/// <summary>Query to retrieve a single contact history record by ID.</summary>
public record GetContactHistoryByIdQuery(Guid Id) : IRequest<Result<ContactHistoryDto>>;
