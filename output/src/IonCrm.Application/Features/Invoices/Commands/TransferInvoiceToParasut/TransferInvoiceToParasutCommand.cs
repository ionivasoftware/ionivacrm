using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Commands.TransferInvoiceToParasut;

/// <summary>
/// Transfers an existing CRM invoice (Draft) to Paraşüt.
/// Sets ParasutId and changes status to TransferredToParasut on success.
/// </summary>
public record TransferInvoiceToParasutCommand(Guid InvoiceId) : IRequest<Result<InvoiceDto>>;
