using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.Invoices.Mappings;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Invoices.Commands.UpdateInvoice;

/// <summary>Handles <see cref="UpdateInvoiceCommand"/>.</summary>
public sealed class UpdateInvoiceCommandHandler
    : IRequestHandler<UpdateInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IParasutProductRepository _productRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateInvoiceCommandHandler> _logger;

    public UpdateInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IParasutProductRepository productRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _productRepository = productRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(
        UpdateInvoiceCommand request, CancellationToken cancellationToken)
    {
        // 1. Load invoice
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
        if (invoice is null)
            return Result<InvoiceDto>.Failure("Fatura bulunamadı.");

        // 2. Authorize
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(invoice.ProjectId))
            return Result<InvoiceDto>.Failure("Bu faturaya erişim yetkiniz yok.");

        // 3. Only Draft invoices may be edited
        if (invoice.Status != InvoiceStatus.Draft)
            return Result<InvoiceDto>.Failure(
                "Sadece taslak (Draft) faturalar düzenlenebilir. " +
                "Paraşüt'e aktarılmış veya resmileştirilmiş faturalar değiştirilemez.");

        // 4. Parse lines and compute totals (discount-aware)
        var lines = InvoiceLineCalculator.ParseLines(request.LinesJson);
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // 5. Apply changes
        invoice.Title = request.Title;
        invoice.Description = request.Description;
        invoice.InvoiceSeries = request.InvoiceSeries;
        invoice.InvoiceNumber = request.InvoiceNumber;
        invoice.IssueDate = DateTime.SpecifyKind(request.IssueDate, DateTimeKind.Utc);
        invoice.DueDate = DateTime.SpecifyKind(request.DueDate, DateTimeKind.Utc);
        invoice.Currency = request.Currency;
        invoice.LinesJson = request.LinesJson;
        invoice.NetTotal = netTotal;
        invoice.GrossTotal = grossTotal;

        try
        {
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

            _logger.LogInformation(
                "Invoice {InvoiceId} updated in project {ProjectId} " +
                "(NetTotal={NetTotal}, GrossTotal={GrossTotal})",
                invoice.Id, invoice.ProjectId, netTotal, grossTotal);

            var dto = invoice.ToDto();
            // Liftdesk kaynaklı faturada seçilen Paraşüt ürünlerini kataloğa öğret — bir sonraki
            // ödeme sync'i aynı ürünü kendiliğinden eşlesin, kullanıcı tekrar seçmesin.
            dto.LearnedProductMappings = await LearnProductMappingsAsync(invoice, lines, cancellationToken);
            return Result<InvoiceDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update invoice {InvoiceId}: {Error}",
                request.InvoiceId, ex.InnerException?.Message ?? ex.Message);
            return Result<InvoiceDto>.Failure(
                $"Fatura güncellenemedi: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    /// <summary>
    /// Kullanıcının fatura satırında yaptığı Paraşüt ürün seçimini katalog bilgisine dönüştürür.
    ///
    /// Neden: Liftdesk ödeme sync'i ürünü yalnız isimle (ParasutProduct.ProductName) eşleştirir.
    /// Katalogda karşılığı olmayan ürün her faturada boş gelir, kullanıcı her seferinde elle seçer
    /// ve seçilmeden Paraşüt'e aktarım yapılamaz. Seçimi kataloğa yazınca döngü kırılır.
    ///
    /// Kurallar — bilinçli olarak muhafazakâr:
    ///  - Yalnız Liftdesk kaynaklı faturalar öğretir (EmsPaymentId dolu). Elle oluşturulan faturadaki
    ///    tek seferlik satırlar kataloğu kirletmez.
    ///  - Anahtar SourceProductName (kullanıcı açıklamayı değiştirse de sync'in gönderdiği ad);
    ///    yoksa Description'a düşülür (eski faturalar).
    ///  - Katalogda zaten DOLU bir eşleme varsa ÜZERİNE YAZILMAZ: Ayarlar'daki açık tanım otoritedir,
    ///    tek faturadaki farklı bir seçim onu sessizce değiştirmemeli.
    ///  - UnitPrice 0 bırakılır ki sync gerçek ödeme tutarını kullansın; TaxRate satırın KDV'sinden
    ///    alınır, 0 ise %20 — katalogdaki 0 KDV, sonraki faturaların %0 KDV ile kesilmesine yol açar.
    ///  - Best-effort: öğrenme hatası fatura güncellemesini ASLA bozmaz.
    /// </summary>
    private async Task<List<string>?> LearnProductMappingsAsync(
        Domain.Entities.Invoice invoice, List<InvoiceLineDto> lines, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(invoice.EmsPaymentId)) return null;

        var learned = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.ParasutProductId)) continue;
            var key = (line.SourceProductName ?? line.Description)?.Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            try
            {
                var existing = await _productRepository.GetByNameAsync(key, ct);
                if (existing is null)
                {
                    await _productRepository.AddAsync(new Domain.Entities.ParasutProduct
                    {
                        ProjectId          = null, // katalog global
                        ProductName        = key,
                        ParasutProductId   = line.ParasutProductId!,
                        ParasutProductName = line.ParasutProductName,
                        UnitPrice          = 0m,
                        TaxRate            = line.VatRate > 0 ? line.VatRate / 100m : 0.20m,
                    }, ct);
                    learned.Add($"{key} → {line.ParasutProductName ?? line.ParasutProductId}");
                }
                else if (string.IsNullOrWhiteSpace(existing.ParasutProductId))
                {
                    existing.ParasutProductId   = line.ParasutProductId!;
                    existing.ParasutProductName = line.ParasutProductName ?? existing.ParasutProductName;
                    if (existing.TaxRate <= 0) existing.TaxRate = line.VatRate > 0 ? line.VatRate / 100m : 0.20m;
                    await _productRepository.UpdateAsync(existing, ct);
                    learned.Add($"{key} → {line.ParasutProductName ?? line.ParasutProductId}");
                }
                else if (!string.Equals(existing.ParasutProductId, line.ParasutProductId, StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "Invoice {InvoiceId}: '{Key}' için katalogda farklı bir Paraşüt ürünü tanımlı ({Existing}); " +
                        "satırdaki seçim ({Picked}) kataloğa yazılmadı.",
                        invoice.Id, key, existing.ParasutProductId, line.ParasutProductId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invoice {InvoiceId}: '{Key}' ürün eşlemesi öğrenilemedi.", invoice.Id, key);
            }
        }

        if (learned.Count > 0)
            _logger.LogInformation("Invoice {InvoiceId}: {Count} Paraşüt ürün eşlemesi öğrenildi: {Items}",
                invoice.Id, learned.Count, string.Join("; ", learned));

        return learned.Count > 0 ? learned : null;
    }
}
