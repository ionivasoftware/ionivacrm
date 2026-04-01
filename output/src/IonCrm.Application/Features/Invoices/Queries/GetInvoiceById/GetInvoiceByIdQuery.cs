using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Queries.GetInvoiceById;

/// <summary>Returns a single invoice by its ID.</summary>
public record GetInvoiceByIdQuery(Guid InvoiceId) : IRequest<Result<InvoiceDto>>;
