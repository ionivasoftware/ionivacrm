using IonCrm.Application.Common.Helpers;

namespace IonCrm.Tests.Reports;

public class CountryInferenceTests
{
    [Theory]
    [InlineData("+90 532 000 00 00", "TR")]
    [InlineData("0090 532 000 00 00", "TR")]
    [InlineData("+44 20 7946 0000", "GB")]
    [InlineData("+356 2122 3344", "MT")]
    [InlineData("+49 (30) 1234567", "DE")]
    [InlineData("+1 212 555 0100", "US")]
    [InlineData("+971 4 123 4567", "AE")]
    public void FromPhone_reads_the_international_prefix(string phone, string expected)
        => CountryInference.FromPhone(phone).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0555 000 00 00")]      // national format says nothing
    [InlineData("532 000 00 00")]
    [InlineData("+90 5")]               // too short to be a number
    [InlineData("+999 1234 5678")]      // unknown prefix
    public void FromPhone_returns_null_when_the_number_tells_nothing(string? phone)
        => CountryInference.FromPhone(phone).Should().BeNull();

    [Fact]
    public void Resolve_prefers_the_record_country_over_the_phone()
        => CountryInference.Resolve(" gb ", "+90 532 000 00 00").Should().Be(("GB", CountryInference.SourceRecord));

    [Fact]
    public void Resolve_falls_back_to_the_phone_and_marks_the_source()
        => CountryInference.Resolve("xx1", "+90 532 000 00 00").Should().Be(("TR", CountryInference.SourcePhone));

    [Fact]
    public void Resolve_is_unknown_when_neither_tells()
        => CountryInference.Resolve(null, "0555 000 00 00").Should().Be(((string?)null, (string?)null));
}
