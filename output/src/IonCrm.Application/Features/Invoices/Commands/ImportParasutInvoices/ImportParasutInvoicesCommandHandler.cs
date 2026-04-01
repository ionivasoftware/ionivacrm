using System.Globalization;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Invoices.Commands.ImportParasutInvoices;

/// <summary>
/// Handles <see cref="ImportParasutInvoicesCommand"/>.
///
/// One-time bulk import: fetches all sales invoices from Paraşüt for each
/// CRM customer that has a linked ParasutContactId, then creates corresponding
/// Invoice records in the CRM database (status = TransferredToParasut).
/// Already-imported invoices (matched by ParasutId) are skipped.
/// </summary>
public sealed class ImportParasutInvoicesCommandHandler
    : IRequestHandler<ImportParasutInvoicesCommand, Result<ImportParasutInvoicesDto>>
{
    private readonly IParasutService _parasutService;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ImportParasutInvoicesCommandHandler> _logger;

    /// <summary>Maximum page size per Paraşüt API request.</summary>
    private const int ParasutPageSize = 25;

    /// <summary>Safety limit to prevent infinite pagination loops.</summary>
    private const int MaxPages = 100;

    public ImportParasutInvoicesCommandHandler(
        IParasutService parasutService,
        IInvoiceRepository invoiceRepository,
        ICustomerRepository customerRepository,
        ICurrentUserService currentUser,
        ILogger<ImportParasutInvoicesCommandHandler> logger)
    {
        _parasutService = parasutService;
        _invoiceRepository = invoiceRepository;
        _customerRepository = customerRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<ImportParasutInvoicesDto>> Handle(
        ImportParasutInvoicesCommand request, CancellationToken cancellationToken)
    {
        // ── Auth check ───────────────────────────────────────────────────────
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(request.ProjectId))
            return Result<ImportParasutInvoicesDto>.Failure("Bu projeye erişim yetkiniz yok.");

        var dto = new ImportParasutInvoicesDto();

        // ── 1. Get all CRM customers with ParasutContactId ───────────────────
        var customers = await _customerRepository.FindAsync(
            c => c.ProjectId == request.ProjectId && c.ParasutContactId != null,
            cancellationToken);

        if (customers.Count == 0)
        {
            _logger.LogInformation(
                "ImportParasutInvoices: no customers with ParasutContactId in project {ProjectId}.",
                request.ProjectId);
            return Result<ImportParasutInvoicesDto>.Success(dto);
        }

        _logger.LogInformation(
            "ImportParasutInvoices: starting import for {Count} customers in project {ProjectId}.",
            customers.Count, request.ProjectId);

        // ── 2. Load existing ParasutIds to skip duplicates ───────────────────
        var existingInvoices = await _invoiceRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);
        var existingParasutIds = existingInvoices
            .Where(i => !string.IsNullOrEmpty(i.ParasutId))
            .Select(i => i.ParasutId!)
            .ToHashSet();

        // ── 3. For each customer, fetch & import their Paraşüt invoices ──────
        foreach (var customer in customers)
        {
            dto.CustomersProcessed++;
            var contactId = customer.ParasutContactId!;

            try
            {
                await ImportCustomerInvoicesAsync(
                    request.ProjectId, customer, contactId,
                    existingParasutIds, dto, cancellationToken);
            }
            catch (Exception ex)
            {
                var msg = $"Müşteri '{customer.CompanyName}' (ParasutContactId={contactId}): {ex.InnerException?.Message ?? ex.Message}";
                dto.Errors.Add(msg);
                dto.FailedCount++;
                _logger.LogError(ex,
                    "ImportParasutInvoices: failed for customer {CustomerId} contact {ContactId}: {Error}",
                    customer.Id, contactId, ex.InnerException?.Message ?? ex.Message);
            }
        }

        _logger.LogInformation(
            "ImportParasutInvoices: completed for project {ProjectId}. Imported={Imported} Skipped={Skipped} Failed={Failed}",
            request.ProjectId, dto.ImportedCount, dto.SkippedCount, dto.FailedCount);

        return Result<ImportParasutInvoicesDto>.Success(dto);
    }

    /// <summary>
    /// Fetches all pages of invoices for a single Paraşüt contact and imports them.
    /// </summary>
    private async Task ImportCustomerInvoicesAsync(
        Guid projectId,
        Customer customer,
        string parasutContactId,
        HashSet<string> existingParasutIds,
        ImportParasutInvoicesDto dto,
        CancellationToken cancellationToken)
    {
        var page = 1;

        while (page <= MaxPages)
        {
            var (data, error) = await _parasutService.GetContactInvoicesAsync(
                projectId, parasutContactId, page, ParasutPageSize, cancellationToken);

            if (error is not null)
            {
                dto.Errors.Add($"Müşteri '{customer.CompanyName}' sayfa {page}: {error}");
                dto.FailedCount++;
                break;
            }

            if (data?.Data is null || data.Data.Count == 0)
                break;

            foreach (var invoiceData in data.Data)
            {
                var parasutId = invoiceData.Id;
                if (string.IsNullOrEmpty(parasutId))
                    continue;

                // Skip already-imported
                if (existingParasutIds.Contains(parasutId))
                {
                    dto.SkippedCount++;
                    continue;
                }

                try
                {
                    var invoice = MapToInvoice(projectId, customer.Id, parasutId, invoiceData);
                    await _invoiceRepository.AddAsync(invoice, cancellationToken);
                    existingParasutIds.Add(parasutId); // prevent duplicates within this run
                    dto.ImportedCount++;
                }
                catch (Exception ex)
                {
                    dto.Errors.Add($"Fatura ParasutId={parasutId}: {ex.InnerException?.Message ?? ex.Message}");
                    dto.FailedCount++;
                    _logger.LogError(ex,
                        "ImportParasutInvoices: failed to save invoice ParasutId={ParasutId}: {Error}",
                        parasutId, ex.InnerException?.Message ?? ex.Message);
                }
            }

            // Check if there are more pages
            var totalPages = data.Meta?.TotalPages ?? 1;
            if (page >= totalPages)
                break;

            page++;
        }
    }

    /// <summary>
    /// Maps a Paraşüt sales invoice JSON:API object to a CRM Invoice entity.
    /// </summary>
    private static Invoice MapToInvoice(
        Guid projectId,
        Guid customerId,
        string parasutId,
        JsonApiDataObject<ParasutSalesInvoiceAttributes> invoiceData)
    {
        var attr = invoiceData.Attributes;

        // Parse dates — Paraşüt returns "yyyy-MM-dd"
        var issueDate = ParseParasutDate(attr.IssueDate);
        var dueDate = ParseParasutDate(attr.DueDate);

        // Parse monetary values — Paraşüt returns strings like "1234.56"
        var grossTotal = ParseDecimal(attr.GrossTotal);
        var netTotal = ParseDecimal(attr.NetTotal);

        // Build a descriptive title from series + number
        var title = BuildTitle(attr.InvoiceSeries, attr.InvoiceId, attr.Description);

        return new Invoice
        {
            ProjectId = projectId,
            CustomerId = customerId,
            Title = title,
            Description = attr.Description,
            InvoiceSeries = attr.InvoiceSeries,
            InvoiceNumber = attr.InvoiceId,
            IssueDate = issueDate,
            DueDate = dueDate,
            Currency = attr.Currency ?? "TRL",
            GrossTotal = grossTotal,
            NetTotal = netTotal,
            LinesJson = "[]", // line items not available from list endpoint
            Status = InvoiceStatus.TransferredToParasut,
            ParasutId = parasutId
        };
    }

    /// <summary>Parses a Paraşüt date string (yyyy-MM-dd) to UTC DateTime.</summary>
    private static DateTime ParseParasutDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    }

    /// <summary>Parses a Paraşüt decimal string (e.g. "1234.56") to decimal.</summary>
    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    /// <summary>Builds a human-readable title from invoice series/number/description.</summary>
    private static string BuildTitle(string? series, int? number, string? description)
    {
        if (!string.IsNullOrEmpty(series) && number.HasValue)
            return $"{series}-{number}";
        if (!string.IsNullOrEmpty(series))
            return series;
        if (number.HasValue)
            return $"Fatura #{number}";
        if (!string.IsNullOrEmpty(description))
            return description.Length > 100 ? description[..100] : description;
        return "Paraşüt Faturası";
    }
}
