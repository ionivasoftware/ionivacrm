using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Features.Invoices;

namespace IonCrm.Tests.Invoices;

/// <summary>
/// Unit tests for <see cref="InvoiceLineCalculator"/>.
/// </summary>
public class InvoiceLineCalculatorTests
{
    // ── ParseLines ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseLines_NullJson_ReturnsEmptyList()
    {
        // Act
        var result = InvoiceLineCalculator.ParseLines(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseLines_EmptyArrayJson_ReturnsEmptyList()
    {
        // Act
        var result = InvoiceLineCalculator.ParseLines("[]");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseLines_WhitespaceJson_ReturnsEmptyList()
    {
        // Act
        var result = InvoiceLineCalculator.ParseLines("   ");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseLines_InvalidJson_ReturnsEmptyList()
    {
        // Act
        var result = InvoiceLineCalculator.ParseLines("not-valid-json{{{");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseLines_ValidJsonArray_ReturnsParsedLines()
    {
        // Arrange
        var json = """
            [
              { "description": "Widget", "quantity": 2, "unitPrice": 100, "vatRate": 20,
                "discountValue": 0, "discountType": "percent" }
            ]
            """;

        // Act
        var result = InvoiceLineCalculator.ParseLines(json);

        // Assert
        result.Should().HaveCount(1);
        result[0].Description.Should().Be("Widget");
        result[0].Quantity.Should().Be(2);
        result[0].UnitPrice.Should().Be(100);
        result[0].VatRate.Should().Be(20);
    }

    [Fact]
    public void ParseLines_IsCaseInsensitive_ParsesLowercaseProperties()
    {
        // Arrange (lowercase property names)
        var json = """[{"description":"Svc","quantity":1,"unitprice":50,"vatrate":10,"discountvalue":0,"discounttype":"percent"}]""";

        // Act
        var result = InvoiceLineCalculator.ParseLines(json);

        // Assert
        result.Should().HaveCount(1);
        result[0].UnitPrice.Should().Be(50);
    }

    // ── ComputeTotals — no discount ───────────────────────────────────────────

    [Fact]
    public void ComputeTotals_SingleLineNoDiscount_ReturnsCorrectNetAndGross()
    {
        // Arrange: 2 items × 100 TL = 200 TL net; VAT 20% = 40 TL → gross 240
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 2, UnitPrice = 100, VatRate = 20, DiscountValue = 0, DiscountType = "percent" }
        };

        // Act
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // Assert
        netTotal.Should().Be(200.00m);
        grossTotal.Should().Be(240.00m);
    }

    [Fact]
    public void ComputeTotals_EmptyLines_ReturnZeros()
    {
        // Act
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(new List<InvoiceLineDto>());

        // Assert
        netTotal.Should().Be(0m);
        grossTotal.Should().Be(0m);
    }

    // ── ComputeTotals — percentage discount ──────────────────────────────────

    [Fact]
    public void ComputeTotals_PercentageDiscount_DeductsCorrectly()
    {
        // Arrange: 1 item × 1000 TL, 10% discount → net 900 TL; VAT 18% → 162 → gross 1062
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 1, UnitPrice = 1000, VatRate = 18, DiscountValue = 10, DiscountType = "percent" }
        };

        // Act
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // Assert
        netTotal.Should().Be(900.00m);
        grossTotal.Should().Be(1062.00m);
    }

    [Fact]
    public void ComputeTotals_PercentageDiscount_CaseInsensitiveType()
    {
        // "PERCENT" should behave identically to "percent"
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 1, UnitPrice = 200, VatRate = 10, DiscountValue = 50, DiscountType = "PERCENT" }
        };

        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // 200 × 50% = 100 discount → net 100; VAT 10% = 10 → gross 110
        netTotal.Should().Be(100.00m);
        grossTotal.Should().Be(110.00m);
    }

    // ── ComputeTotals — fixed-amount discount ────────────────────────────────

    [Fact]
    public void ComputeTotals_AmountDiscount_DeductsFixedValue()
    {
        // Arrange: 1 item × 500 TL, 50 TL amount discount → net 450; VAT 20% = 90 → gross 540
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 1, UnitPrice = 500, VatRate = 20, DiscountValue = 50, DiscountType = "amount" }
        };

        // Act
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // Assert
        netTotal.Should().Be(450.00m);
        grossTotal.Should().Be(540.00m);
    }

    [Fact]
    public void ComputeTotals_AmountDiscount_CaseInsensitiveType()
    {
        // "AMOUNT" should behave identically to "amount"
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 2, UnitPrice = 100, VatRate = 0, DiscountValue = 30, DiscountType = "AMOUNT" }
        };

        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // subtotal 200 - 30 discount → net 170; VAT 0% → gross 170
        netTotal.Should().Be(170.00m);
        grossTotal.Should().Be(170.00m);
    }

    // ── ComputeTotals — discount guard (cannot exceed subtotal) ──────────────

    [Fact]
    public void ComputeTotals_AmountDiscountExceedsSubtotal_ClampsToSubtotal()
    {
        // Arrange: subtotal 100 TL but 200 TL fixed discount → clamped to 100 → net 0
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 1, UnitPrice = 100, VatRate = 20, DiscountValue = 200, DiscountType = "amount" }
        };

        // Act
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // Assert — no negative totals
        netTotal.Should().Be(0m);
        grossTotal.Should().Be(0m);
    }

    [Fact]
    public void ComputeTotals_PercentageDiscount100Percent_ResultsInZeroNet()
    {
        // 100% discount → net line = 0, gross = 0
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 5, UnitPrice = 50, VatRate = 18, DiscountValue = 100, DiscountType = "percent" }
        };

        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        netTotal.Should().Be(0m);
        grossTotal.Should().Be(0m);
    }

    // ── ComputeTotals — multiple lines ───────────────────────────────────────

    [Fact]
    public void ComputeTotals_MultipleLines_AggregatesCorrectly()
    {
        // Line 1: 1 × 100, VAT 20%, no discount → net 100, gross 120
        // Line 2: 2 × 50,  VAT 10%, 10% discount → subtotal 100, discount 10 → net 90, VAT 9 → gross 99
        // Total: net 190, gross 219
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 1, UnitPrice = 100, VatRate = 20, DiscountValue = 0,  DiscountType = "percent" },
            new() { Quantity = 2, UnitPrice = 50,  VatRate = 10, DiscountValue = 10, DiscountType = "percent" }
        };

        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        netTotal.Should().Be(190.00m);
        grossTotal.Should().Be(219.00m);
    }

    // ── ComputeTotals — rounding ──────────────────────────────────────────────

    [Fact]
    public void ComputeTotals_ResultsAreRoundedToTwoDecimalPlaces()
    {
        // 1 × 33.33, no discount, VAT 20% → net 33.33; gross 33.33 × 1.2 = 39.996 → rounds to 40.00
        var lines = new List<InvoiceLineDto>
        {
            new() { Quantity = 1, UnitPrice = 33.33m, VatRate = 20, DiscountValue = 0, DiscountType = "percent" }
        };

        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        netTotal.Should().Be(33.33m);
        grossTotal.Should().Be(40.00m); // 33.33 + 6.666 = 39.996 → rounded to 40.00
    }
}
