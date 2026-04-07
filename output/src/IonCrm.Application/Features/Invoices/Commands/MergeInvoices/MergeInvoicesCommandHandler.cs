using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.Invoices.Mappings;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Application.Features.Invoices.Commands.MergeInvoices;

/// <summary>Handles <see cref="MergeInvoicesCommand"/>.</summary>
public sealed class MergeInvoicesCommandHandler
    : IRequestHandler<MergeInvoicesCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<MergeInvoicesCommandHandler> _logger;

    public MergeInvoicesCommandHandler(
        IInvoiceRepository invoiceRepository,
        ICurrentUserService currentUser,
        ILogger<MergeInvoicesCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _currentUser       = currentUser;
        _logger            = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(
        MergeInvoicesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.InvoiceIds is null || request.InvoiceIds.Count < 2)
            return Result<InvoiceDto>.Failure("Birleştirmek için en az 2 fatura seçilmelidir.");

        // 1. Load all invoices
        var invoices = new List<Invoice>();
        foreach (var id in request.InvoiceIds)
        {
            var inv = await _invoiceRepository.GetByIdAsync(id, cancellationToken);
            if (inv is null)
                return Result<InvoiceDto>.Failure($"Fatura bulunamadı: {id}");
            invoices.Add(inv);
        }

        // 2. Authorization
        foreach (var inv in invoices)
        {
            if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(inv.ProjectId))
                return Result<InvoiceDto>.Failure($"Bu faturaya erişim yetkiniz yok: {inv.Id}");
        }

        // 3. All must be Draft
        if (invoices.Any(i => i.Status != InvoiceStatus.Draft))
            return Result<InvoiceDto>.Failure("Sadece taslak (Draft) faturalar birleştirilebilir.");

        // 4. Same customer
        var customerId = invoices[0].CustomerId;
        if (invoices.Any(i => i.CustomerId != customerId))
            return Result<InvoiceDto>.Failure("Yalnızca aynı müşteriye ait taslaklar birleştirilebilir.");

        // 5. Same project
        var projectId = invoices[0].ProjectId;
        if (invoices.Any(i => i.ProjectId != projectId))
            return Result<InvoiceDto>.Failure("Yalnızca aynı projeye ait taslaklar birleştirilebilir.");

        // 6. Merge LinesJson — concatenate all line arrays
        var allLines = new List<object>();
        foreach (var inv in invoices)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<JsonElement>>(inv.LinesJson);
                if (parsed is not null)
                    allLines.AddRange(parsed.Cast<object>());
            }
            catch
            {
                // skip unparseable lines — invoice may have been created with empty/invalid json
            }
        }

        var mergedLinesJson = JsonSerializer.Serialize(allLines);

        // 7. Recompute totals from merged lines
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(
            InvoiceLineCalculator.ParseLines(mergedLinesJson));

        // 8. Build merged invoice
        var currency   = invoices[0].Currency;
        var issueDate  = invoices.Min(i => i.IssueDate);
        var dueDate    = invoices.Max(i => i.DueDate);
        var title      = request.Title ?? $"Birleştirilmiş Taslak ({invoices.Count} fatura)";

        var merged = new Invoice
        {
            ProjectId   = projectId,
            CustomerId  = customerId,
            Title       = title,
            Description = string.Join(" | ", invoices.Select(i => i.Title)),
            IssueDate   = issueDate,
            DueDate     = dueDate,
            Currency    = currency,
            NetTotal    = netTotal,
            GrossTotal  = grossTotal,
            LinesJson   = mergedLinesJson,
            Status      = InvoiceStatus.Draft,
        };

        var created = await _invoiceRepository.AddAsync(merged, cancellationToken);

        // 9. Soft-delete source invoices
        foreach (var inv in invoices)
            await _invoiceRepository.DeleteAsync(inv, cancellationToken);

        _logger.LogInformation(
            "Merged {Count} invoice drafts into invoice {MergedId} for customer {CustomerId}.",
            invoices.Count, created.Id, customerId);

        // Reload with navigation properties for DTO mapping
        var result = await _invoiceRepository.GetByIdAsync(created.Id, cancellationToken);
        return Result<InvoiceDto>.Success(result!.ToDto());
    }
}
