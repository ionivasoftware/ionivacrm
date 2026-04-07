using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Commands.MergeInvoices;

/// <summary>
/// Merges two or more Draft invoices for the same customer into a single new Draft invoice.
/// All source invoices must be Draft status and belong to the same customer and project.
/// The source invoices are soft-deleted after the merged invoice is created.
/// </summary>
public record MergeInvoicesCommand(
    List<Guid> InvoiceIds,
    string? Title = null) : IRequest<Result<InvoiceDto>>;
