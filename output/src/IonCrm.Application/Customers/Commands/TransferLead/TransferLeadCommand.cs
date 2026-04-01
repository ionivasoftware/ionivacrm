using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Commands.TransferLead;

/// <summary>
/// Command to transfer all records (ContactHistories, Tasks, Opportunities)
/// from a Lead customer to an active target customer, then soft-delete the lead.
/// </summary>
/// <param name="LeadId">The ID of the Lead customer whose records will be transferred.</param>
/// <param name="TargetCustomerId">The ID of the active customer who will receive the records.</param>
public record TransferLeadCommand(Guid LeadId, Guid TargetCustomerId) : IRequest<Result>;
