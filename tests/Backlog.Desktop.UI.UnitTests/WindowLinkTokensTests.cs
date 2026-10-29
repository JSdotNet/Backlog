using Backlog.Desktop.UI.Mcp;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The link token that guards the pop-out pages the desktop app serves (local ADR
/// 0043 §7): minted for one link, redeemed once, dead after ten minutes, and
/// exchanged on redemption for the cookie value a page's later requests carry.
/// </summary>
public sealed class WindowLinkTokensTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Every_minted_token_is_a_new_url_safe_secret()
    {
        var tokens = new WindowLinkTokens(_clock);

        var minted = Enumerable.Range(0, 50).Select(_ => tokens.Mint()).ToList();

        Assert.Equal(minted.Count, minted.Distinct(StringComparer.Ordinal).Count());
        Assert.All(minted, token =>
        {
            Assert.True(token.Length >= 43, $"A token of {token.Length} characters is shorter than 256 bits.");
            Assert.Equal(Uri.EscapeDataString(token), token);
        });
    }

    [Fact]
    public void A_minted_token_redeems_once_for_a_cookie_value_a_page_is_then_served_on()
    {
        var tokens = new WindowLinkTokens(_clock);
        var token = tokens.Mint();

        var cookie = tokens.Redeem(token);

        Assert.NotNull(cookie);
        Assert.NotEqual(token, cookie);
        Assert.True(tokens.IsValidCookie(cookie));
    }

    [Fact]
    public void A_token_redeems_only_once()
    {
        var tokens = new WindowLinkTokens(_clock);
        var token = tokens.Mint();

        Assert.NotNull(tokens.Redeem(token));
        Assert.Null(tokens.Redeem(token));
    }

    [Fact]
    public void A_token_is_valid_for_ten_minutes_and_no_longer()
    {
        var tokens = new WindowLinkTokens(_clock);
        var lastMoment = tokens.Mint();
        var late = tokens.Mint();

        _clock.Advance(WindowLinkTokens.Lifetime);
        Assert.NotNull(tokens.Redeem(lastMoment));

        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.Null(tokens.Redeem(late));
    }

    [Fact]
    public void An_expired_token_never_redeems()
    {
        var tokens = new WindowLinkTokens(_clock);
        var token = tokens.Mint();

        _clock.Advance(WindowLinkTokens.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(tokens.Redeem(token));
        Assert.Null(tokens.Redeem(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("never-minted")]
    public void Anything_never_minted_redeems_nothing(string? token)
    {
        var tokens = new WindowLinkTokens(_clock);
        tokens.Mint();

        Assert.Null(tokens.Redeem(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("never-issued")]
    public void A_cookie_value_never_issued_is_not_valid(string? cookie)
    {
        var tokens = new WindowLinkTokens(_clock);
        var token = tokens.Mint();

        Assert.False(tokens.IsValidCookie(cookie));
        Assert.False(tokens.IsValidCookie(token), "An unredeemed token is not a cookie value.");
    }

    [Fact]
    public void A_cookie_value_outlives_the_token_it_was_exchanged_for()
    {
        var tokens = new WindowLinkTokens(_clock);
        var cookie = tokens.Redeem(tokens.Mint());

        _clock.Advance(TimeSpan.FromDays(3));

        Assert.True(tokens.IsValidCookie(cookie));
    }

    [Fact]
    public void Another_instance_redeems_none_of_this_ones_tokens()
    {
        var token = new WindowLinkTokens(_clock).Mint();

        Assert.Null(new WindowLinkTokens(_clock).Redeem(token));
    }
}
