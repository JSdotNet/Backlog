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

        _clock.Advance(WindowLinkTokens.Lifetime + TimeSpan.FromHours(1));

        Assert.True(tokens.IsValidCookie(cookie));
    }

    /// <summary>The cookie a link buys expires after 8 idle hours (local ADR 0052 §4),
    /// and once it has, it never comes back.</summary>
    [Fact]
    public void A_cookie_value_idle_for_eight_hours_is_valid_to_the_last_moment_and_then_never_again()
    {
        var tokens = new WindowLinkTokens(_clock);
        var lastMoment = tokens.Redeem(tokens.Mint());
        var late = tokens.Redeem(tokens.Mint());

        _clock.Advance(WindowLinkTokens.IdleTimeout);
        Assert.True(tokens.IsValidCookie(lastMoment));

        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.False(tokens.IsValidCookie(late));

        using (tokens.Hold(late))
        {
            Assert.False(tokens.IsValidCookie(late), "An expired cookie value is forgotten, so not even a hold revives it.");
        }
    }

    [Fact]
    public void Every_use_of_a_cookie_value_starts_its_idle_time_over()
    {
        var tokens = new WindowLinkTokens(_clock);
        var cookie = tokens.Redeem(tokens.Mint());

        for (var day = 0; day < 3; day++)
        {
            _clock.Advance(WindowLinkTokens.IdleTimeout - TimeSpan.FromMinutes(1));
            Assert.True(tokens.IsValidCookie(cookie));
        }

        _clock.Advance(WindowLinkTokens.IdleTimeout + TimeSpan.FromMinutes(1));
        Assert.False(tokens.IsValidCookie(cookie));
    }

    /// <summary>A page whose circuit is connected holds its cookie for as long as the
    /// connection lasts, so a window in use never expires under the person; the idle
    /// time starts when the connection ends.</summary>
    [Fact]
    public void A_held_cookie_value_is_not_idle_until_its_hold_ends()
    {
        var tokens = new WindowLinkTokens(_clock);
        var cookie = tokens.Redeem(tokens.Mint());

        var circuit = tokens.Hold(cookie);
        _clock.Advance(WindowLinkTokens.IdleTimeout * 3);
        Assert.True(tokens.IsValidCookie(cookie));

        circuit.Dispose();
        circuit.Dispose();
        _clock.Advance(WindowLinkTokens.IdleTimeout);
        Assert.True(tokens.IsValidCookie(cookie));

        _clock.Advance(WindowLinkTokens.IdleTimeout + TimeSpan.FromTicks(1));
        Assert.False(tokens.IsValidCookie(cookie));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("never-issued")]
    public void A_hold_on_a_cookie_value_never_issued_holds_nothing(string? cookie)
    {
        var tokens = new WindowLinkTokens(_clock);

        using (tokens.Hold(cookie))
        {
            Assert.False(tokens.IsValidCookie(cookie));
        }
    }

    [Fact]
    public void Minting_answers_when_the_token_stops_redeeming()
    {
        var tokens = new WindowLinkTokens(_clock);

        tokens.Mint(out var expires);

        Assert.Equal(_clock.GetUtcNow() + WindowLinkTokens.Lifetime, expires);
    }

    [Fact]
    public void Another_instance_redeems_none_of_this_ones_tokens()
    {
        var token = new WindowLinkTokens(_clock).Mint();

        Assert.Null(new WindowLinkTokens(_clock).Redeem(token));
    }
}
