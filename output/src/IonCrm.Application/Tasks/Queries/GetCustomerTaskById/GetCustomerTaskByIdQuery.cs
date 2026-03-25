using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetCustomerTaskById;

/// <summary>Query to retrieve a single customer task by ID.</summary>
public record GetCustomerTaskByIdQuery(Guid Id) : IRequest<Result<CustomerTaskDto>>;
