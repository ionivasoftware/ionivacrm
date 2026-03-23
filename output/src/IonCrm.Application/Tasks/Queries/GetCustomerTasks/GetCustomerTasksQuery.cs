using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Tasks.Queries.GetCustomerTasks;

/// <summary>Query to retrieve all tasks for a customer.</summary>
public record GetCustomerTasksQuery(Guid CustomerId) : IRequest<Result<IReadOnlyList<CustomerTaskDto>>>;
