using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Queries.GetCustomerParasutTransactions;

/// <summary>Handles <see cref="GetCustomerParasutTransactionsQuery"/>.</summary>
public sealed class GetCustomerParasutTransactionsQueryHandler
    : IRequestHandler<GetCustomerParasutTransactionsQuery, Result<CustomerParasutTransactionsDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IParasutService     _parasutService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GetCustomerParasutTransactionsQueryHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="GetCustomerParasutTransactionsQueryHandler"/>.</summary>
    public GetCustomerParasutTransactionsQueryHandler(
        ICustomerRepository customerRepository,
        IParasutService     parasutService,
        ICurrentUserService currentUser,
        ILogger<GetCustomerParasutTransactionsQueryHandler> logger)
    {
        _customerRepository = customerRepository;
        _parasutService     = parasutService;
        _currentUser        = currentUser;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerParasutTransactionsDto>> Handle(
        GetCustomerParasutTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        // 1 — Fetch the customer and enforce tenant isolation
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<CustomerParasutTransactionsDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<CustomerParasutTransactionsDto>.Failure("Müşteri bulunamadı.");

        // 2 — Customer must be linked to a Paraşüt contact
        if (string.IsNullOrWhiteSpace(customer.ParasutContactId))
            return Result<CustomerParasutTransactionsDto>.Failure(
                "Müşteri henüz Paraşüt'e bağlanmamış. Lütfen önce cariyi Paraşüt ile eşleştirin.");

        // 3 — Fetch both invoices and transactions concurrently
        var invoicesTask = _parasutService.GetContactInvoicesAsync(
            customer.ProjectId,
            customer.ParasutContactId,
            request.Page,
            request.PageSize,
            cancellationToken);

        var transactionsTask = _parasutService.GetContactTransactionsAsync(
            customer.ProjectId,
            customer.ParasutContactId,
            request.Page,
            request.PageSize,
            cancellationToken);

        await Task.WhenAll(invoicesTask, transactionsTask);

        var (invoiceData, invoiceError) = invoicesTask.Result;
        var (txnData, txnError) = transactionsTask.Result;

        // Log warnings but don't fail entirely if one call fails — return partial data
        if (invoiceData is null)
        {
            _logger.LogWarning(
                "Paraşüt cari faturaları alınamadı. CustomerId={CustomerId} ContactId={ContactId} Error={Error}",
                request.CustomerId, customer.ParasutContactId, invoiceError);
        }

        if (txnData is null)
        {
            _logger.LogWarning(
                "Paraşüt cari hareketleri alınamadı. CustomerId={CustomerId} ContactId={ContactId} Error={Error}",
                request.CustomerId, customer.ParasutContactId, txnError);
        }

        // If both failed, return error
        if (invoiceData is null && txnData is null)
        {
            return Result<CustomerParasutTransactionsDto>.Failure(
                invoiceError ?? txnError ?? "Cari hareketleri alınamadı.");
        }

        // 4 — Map Paraşüt JSON:API responses to DTOs
        static decimal Parse(string? s) =>
            decimal.TryParse(s,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v) ? v : 0m;

        var invoices = invoiceData?.Data.Select(d => new ParasutInvoiceItemDto(
            Id:              d.Id ?? string.Empty,
            IssueDate:       d.Attributes.IssueDate,
            DueDate:         d.Attributes.DueDate,
            Currency:        d.Attributes.Currency,
            GrossTotal:      Parse(d.Attributes.GrossTotal),
            NetTotal:        Parse(d.Attributes.NetTotal),
            TotalPaid:       Parse(d.Attributes.TotalPaid),
            Remaining:       Parse(d.Attributes.Remaining),
            Description:     d.Attributes.Description,
            ArchivingStatus: d.Attributes.ArchivingStatus
        )).ToList() ?? new List<ParasutInvoiceItemDto>();

        var transactions = txnData?.Data.Select(d => new ParasutTransactionItemDto(
            Date:            d.Attributes.Date,
            Amount:          Parse(d.Attributes.Amount),
            Currency:        d.Attributes.Currency,
            TransactionType: d.Attributes.TransactionType ?? "unknown",
            Description:     d.Attributes.Description,
            PayableType:     d.Attributes.PayableType,
            PayableId:       d.Attributes.PayableId,
            Remaining:       Parse(d.Attributes.Remaining)
        )).ToList() ?? new List<ParasutTransactionItemDto>();

        return Result<CustomerParasutTransactionsDto>.Success(new CustomerParasutTransactionsDto(
            Invoices:              invoices,
            Transactions:          transactions,
            InvoiceTotalCount:     invoiceData?.Meta?.TotalCount ?? invoices.Count,
            TransactionTotalCount: txnData?.Meta?.TotalCount ?? transactions.Count,
            TotalPages:            Math.Max(
                invoiceData?.Meta?.TotalPages ?? 1,
                txnData?.Meta?.TotalPages ?? 1),
            CurrentPage:           request.Page));
    }
}
