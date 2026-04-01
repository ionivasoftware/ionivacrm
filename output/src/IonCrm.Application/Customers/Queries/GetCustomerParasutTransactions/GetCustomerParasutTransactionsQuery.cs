using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerParasutTransactions;

/// <summary>
/// Returns Paraşüt cari hareketleri (invoices + transactions) for a CRM customer.
///
/// The handler resolves the customer's <c>ParasutContactId</c> from the database and
/// delegates the actual Paraşüt API calls to <see cref="IParasutService"/>.
/// Returns a failure result when the customer is not found or not linked to Paraşüt.
/// </summary>
public record GetCustomerParasutTransactionsQuery(
    Guid CustomerId,
    int  Page     = 1,
    int  PageSize = 25
) : IRequest<Result<CustomerParasutTransactionsDto>>;

/// <summary>Combined response with both invoices and debit/credit transactions.</summary>
public record CustomerParasutTransactionsDto(
    List<ParasutInvoiceItemDto> Invoices,
    List<ParasutTransactionItemDto> Transactions,
    int InvoiceTotalCount,
    int TransactionTotalCount,
    int TotalPages,
    int CurrentPage
);

/// <summary>Single invoice item in the list.</summary>
public record ParasutInvoiceItemDto(
    string  Id,
    string  IssueDate,
    string  DueDate,
    string  Currency,
    decimal GrossTotal,
    decimal NetTotal,
    decimal TotalPaid,
    decimal Remaining,
    string? Description,
    string? ArchivingStatus
);

/// <summary>Single debit/credit transaction item (cari hareket).</summary>
public record ParasutTransactionItemDto(
    string  Date,
    decimal Amount,
    string? Currency,
    string  TransactionType,   // "debit" (borç) | "credit" (alacak/tahsilat)
    string? Description,
    string? PayableType,       // "SalesInvoice", "Payment", etc.
    int?    PayableId,
    decimal Remaining
);
