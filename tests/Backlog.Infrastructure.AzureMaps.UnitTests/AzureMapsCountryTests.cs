namespace Backlog.Infrastructure.AzureMaps.UnitTests;

/// <summary>
/// The address's country is free text (<c>domain.md#address</c>, whose example is "Belgium"),
/// and Get Geocoding's <c>countryRegion</c> takes an ISO 3166-1 alpha-2 code. The adapter turns
/// the typed country into its code — an alpha-2 code as is, an English or native name ignoring
/// case and accents — and sends no country at all when it names none, rather than a value the
/// service cannot read (local ADR 0061 §1, §5).
/// </summary>
public sealed class AzureMapsCountryTests
{
    [Theory]
    [InlineData("NL", "NL")]
    [InlineData("be", "BE")]
    [InlineData(" fr ", "FR")]
    [InlineData("Belgium", "BE")]
    [InlineData("België", "BE")]
    [InlineData("belgie", "BE")]
    [InlineData("Belgique", "BE")]
    [InlineData("Nederland", "NL")]
    [InlineData("Netherlands", "NL")]
    [InlineData("the Netherlands", "NL")]
    [InlineData("The Netherlands", "NL")]
    [InlineData("Deutschland", "DE")]
    [InlineData("germany", "DE")]
    [InlineData("España", "ES")]
    [InlineData("United Kingdom", "GB")]
    [InlineData("BEL", "BE")]
    public void A_country_reads_as_its_alpha_2_code(string typed, string code) =>
        Assert.Equal(code, AzureMapsCountry.CodeOf(typed));

    [Theory]
    [InlineData("Atlantis")]
    [InlineData("XX")]
    [InlineData("Benelux")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_country_that_names_none_has_no_code(string typed) =>
        Assert.Null(AzureMapsCountry.CodeOf(typed));
}
