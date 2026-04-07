using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Sync.Commands.SyncEmsPayments;

/// <summary>
/// Fetches recent completed payments from all EMS-connected projects and auto-creates
/// invoice drafts in the CRM for any payment not yet recorded.
///
/// Logic per project:
///   1. Call GET /api/v1/crm/payments/recent (CompletionPayment=1, last <see cref="WindowMinutes"/> min)
///   2. For each payment, look up the customer by EMS companyId (LegacyId)
///   3. Check for duplicate: skip if an invoice draft with the same EmsPaymentId already exists
///   4. Find the matching ParasutProduct by EmsProductId (optional — falls back to raw price)
///   5. Persist a Draft Invoice in the CRM
/// </summary>
public record SyncEmsPaymentsCommand(
    /// <summary>How far back to look for payments (minutes). Defaults to 20.</summary>
    int WindowMinutes = 20
) : IRequest<Result<SyncEmsPaymentsResult>>;

/// <summary>Summary returned after the sync run.</summary>
public record SyncEmsPaymentsResult(
    int ProjectsScanned,
    int PaymentsFetched,
    int InvoicesCreated,
    int Skipped,
    List<string> Errors);
