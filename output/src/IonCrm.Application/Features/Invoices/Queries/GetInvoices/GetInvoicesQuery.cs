using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Queries.GetInvoices;

/// <summary>Returns all invoices for the given project, newest first.</summary>
public record GetInvoicesQuery(Guid ProjectId) : IRequest<Result<List<InvoiceDto>>>;
