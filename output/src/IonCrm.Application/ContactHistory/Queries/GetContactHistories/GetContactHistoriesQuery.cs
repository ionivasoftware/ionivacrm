using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.ContactHistory.Queries.GetContactHistories;

/// <summary>Query to retrieve all contact history records for a customer.</summary>
public record GetContactHistoriesQuery(Guid CustomerId) : IRequest<Result<IReadOnlyList<ContactHistoryDto>>>;
