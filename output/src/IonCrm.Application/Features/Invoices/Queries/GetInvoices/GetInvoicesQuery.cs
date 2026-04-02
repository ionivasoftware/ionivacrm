using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Queries.GetInvoices;

/// <summary>
/// Returns invoices, newest first.
/// When <see cref="ProjectId"/> is provided, scopes results to that project.
/// When null, returns invoices from all projects the current user is authorised for
/// (SuperAdmin: all projects, regular users: their own project list).
/// </summary>
public record GetInvoicesQuery(Guid? ProjectId) : IRequest<Result<List<InvoiceDto>>>;
