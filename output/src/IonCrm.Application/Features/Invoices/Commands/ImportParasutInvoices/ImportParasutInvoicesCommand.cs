using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Commands.ImportParasutInvoices;

/// <summary>
/// One-time import of existing Paraşüt sales invoices into the CRM Invoice table.
///
/// Flow:
///   1. Load all CRM customers in the project that have a ParasutContactId
///   2. For each customer, fetch their Paraşüt invoices (all pages)
///   3. Skip invoices whose ParasutId already exists in CRM
///   4. Create Invoice entities with Status = TransferredToParasut
/// </summary>
public sealed record ImportParasutInvoicesCommand(Guid ProjectId)
    : IRequest<Result<ImportParasutInvoicesDto>>;

/// <summary>Result DTO for the import operation.</summary>
public sealed class ImportParasutInvoicesDto
{
    /// <summary>Number of invoices successfully imported.</summary>
    public int ImportedCount { get; set; }

    /// <summary>Number of invoices skipped (already existed in CRM).</summary>
    public int SkippedCount { get; set; }

    /// <summary>Number of invoices that failed to import.</summary>
    public int FailedCount { get; set; }

    /// <summary>Number of CRM customers processed (those with ParasutContactId).</summary>
    public int CustomersProcessed { get; set; }

    /// <summary>Error messages for failed imports, if any.</summary>
    public List<string> Errors { get; set; } = new();
}
