using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Commands.DeleteInvoice;

/// <summary>Soft-deletes a Draft invoice. Non-Draft invoices cannot be deleted.</summary>
public record DeleteInvoiceCommand(Guid InvoiceId) : IRequest<Result<bool>>;
