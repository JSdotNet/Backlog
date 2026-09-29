namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The one mechanism every module's enum vocabulary is read through: a token on
/// the wire or in the store to an enum member and back. The tokens themselves
/// are each module's; what is pinned here is what they all share — how a token
/// is normalized before it is looked up, and that an unknown one throws unless
/// the caller names what it stands for instead.
/// </summary>
public sealed class WireTokenMapTests
{
    public enum Shade
    {
        Light,
        InBetween,
        Dark,
        Unmapped
    }

    private static readonly WireTokenMap<Shade> Shades = new(
        "shade",
        new Dictionary<Shade, string>
        {
            [Shade.Light] = "light",
            [Shade.InBetween] = "in_between",
            [Shade.Dark] = "dark"
        },
        aliases: new Dictionary<string, Shade> { ["dim"] = Shade.Dark });

    [Theory]
    [InlineData(Shade.Light, "light")]
    [InlineData(Shade.InBetween, "in_between")]
    [InlineData(Shade.Dark, "dark")]
    public void A_member_is_written_as_its_token(Shade member, string token) =>
        Assert.Equal(token, Shades.ToWire(member));

    [Fact]
    public void A_member_with_no_token_cannot_be_written()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => Shades.ToWire(Shade.Unmapped));

        Assert.Equal("value", thrown.ParamName);
    }

    [Theory]
    [InlineData("light", Shade.Light)]
    [InlineData("in_between", Shade.InBetween)]
    [InlineData("in-between", Shade.InBetween)]
    [InlineData("InBetween", Shade.InBetween)]
    [InlineData("  IN_BETWEEN \t", Shade.InBetween)]
    [InlineData("Dark", Shade.Dark)]
    [InlineData("dim", Shade.Dark)]
    public void A_token_is_read_whatever_its_case_spacing_or_separators(string token, Shade member) =>
        Assert.Equal(member, Shades.Parse(token));

    [Theory]
    [InlineData("  In_Pro-gress ", "inprogress")]
    [InlineData("claude-artifact", "claudeartifact")]
    [InlineData(null, "")]
    public void Normalizing_trims_lower_cases_and_drops_underscores_and_hyphens(string? token, string normalized) =>
        Assert.Equal(normalized, WireTokenMap<Shade>.Normalize(token));

    [Theory]
    [InlineData("lite")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_token_throws_and_says_what_it_was_read_as(string? token)
    {
        var thrown = Assert.Throws<FormatException>(() => Shades.Parse(token));

        Assert.Equal($"Unknown shade '{token}'.", thrown.Message);
    }

    [Theory]
    [InlineData("lite")]
    [InlineData(null)]
    public void An_unknown_token_is_the_fallback_when_the_caller_names_one(string? token) =>
        Assert.Equal(Shade.Light, Shades.Parse(token, fallback: Shade.Light));

    [Fact]
    public void A_fallback_never_outranks_a_token_that_is_known() =>
        Assert.Equal(Shade.Dark, Shades.Parse("dark", fallback: Shade.Light));

    /// <summary>Two tokens that normalize to the same key would make one of them
    /// unreadable, so the table refuses to be built rather than picking one.</summary>
    [Fact]
    public void Two_tokens_that_read_the_same_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new WireTokenMap<Shade>(
            "shade",
            new Dictionary<Shade, string> { [Shade.Light] = "in_between", [Shade.InBetween] = "in-between" }));
    }
}
