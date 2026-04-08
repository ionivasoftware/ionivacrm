using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Sync.Commands.SyncRezervalContractInvoices;

/// <summary>
/// Generates monthly draft invoices for all <c>EftWire</c> Rezerval customer contracts
/// whose <c>NextInvoiceDate</c> is on or before today and whose contract has not ended.
///
/// Logic:
///   1. Load all Active EftWire contracts due today.
///   2. For each contract:
///      - Look up the project's "RezervAl Aylık Lisans Bedeli" Paraşüt product mapping.
///      - Auto-enrich product data from Paraşüt API if incomplete.
///      - Compose dedup key "CONTRACT-{contractId}-{yyyyMM}" stored in Invoice.EmsPaymentId.
///      - Skip if invoice already exists for this contract+month (idempotent).
///      - Create Draft invoice priced at the contract's MonthlyAmount.
///      - Advance NextInvoiceDate by one month (start-day logic with end-of-month fallback).
///      - If new NextInvoiceDate exceeds EndDate, mark contract Completed.
/// </summary>
public record SyncRezervalContractInvoicesCommand : IRequest<Result<SyncRezervalContractInvoicesResult>>;

/// <summary>Summary returned after the contract invoice sync run.</summary>
public record SyncRezervalContractInvoicesResult(
    int ContractsScanned,
    int InvoicesCreated,
    int Skipped,
    int ContractsCompleted,
    List<string> Errors);
