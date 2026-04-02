using System.Text.Json;
using IonCrm.Application.Common.DTOs;

namespace IonCrm.Application.Features.Invoices;

/// <summary>
/// Shared helper for parsing invoice lines from JSON and computing totals.
/// Used by CreateInvoice, UpdateInvoice and TransferInvoiceToParasut handlers.
/// </summary>
public static class InvoiceLineCalculator
{
    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Deserializes <paramref name="linesJson"/> into a list of <see cref="InvoiceLineDto"/>.
    /// Returns an empty list if the JSON is null / empty / invalid.
    /// </summary>
    public static List<InvoiceLineDto> ParseLines(string? linesJson)
    {
        if (string.IsNullOrWhiteSpace(linesJson) || linesJson == "[]")
            return new List<InvoiceLineDto>();

        try
        {
            return JsonSerializer.Deserialize<List<InvoiceLineDto>>(linesJson, _opts)
                   ?? new List<InvoiceLineDto>();
        }
        catch
        {
            return new List<InvoiceLineDto>();
        }
    }

    /// <summary>
    /// Computes <c>NetTotal</c> and <c>GrossTotal</c> from a list of line items.
    ///
    /// For each line:
    ///   lineSubtotal  = Quantity × UnitPrice
    ///   discountAmt   = "percent" → lineSubtotal × (DiscountValue / 100)
    ///                   "amount"  → DiscountValue
    ///   netLine       = lineSubtotal - discountAmt
    ///   vatAmt        = netLine × (VatRate / 100)
    ///   grossLine     = netLine + vatAmt
    ///
    /// NetTotal  = Σ netLine   (subtotal after discount, before VAT)
    /// GrossTotal = Σ grossLine (subtotal after discount + VAT)
    ///
    /// Results are rounded to 2 decimal places.
    /// </summary>
    public static (decimal NetTotal, decimal GrossTotal) ComputeTotals(
        IEnumerable<InvoiceLineDto> lines)
    {
        decimal netTotal = 0m;
        decimal grossTotal = 0m;

        foreach (var line in lines)
        {
            var lineSubtotal = line.Quantity * line.UnitPrice;

            var discountAmount = string.Equals(line.DiscountType, "amount",
                StringComparison.OrdinalIgnoreCase)
                ? line.DiscountValue
                : lineSubtotal * (line.DiscountValue / 100m);

            // Guard: discount cannot exceed the line subtotal
            discountAmount = Math.Min(discountAmount, lineSubtotal);

            var netLine = lineSubtotal - discountAmount;
            var vatAmount = netLine * (line.VatRate / 100m);

            netTotal   += netLine;
            grossTotal += netLine + vatAmount;
        }

        return (Math.Round(netTotal, 2), Math.Round(grossTotal, 2));
    }
}
