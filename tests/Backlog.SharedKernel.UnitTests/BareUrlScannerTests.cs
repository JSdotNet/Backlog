using Backlog.SharedKernel.Markdown;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The one place a bare URL is recognised in prose — a task title and a Markdown
/// body read it the same way, so what counts as a link and where it ends is pinned
/// here once. The ends follow GFM's extended autolinks: punctuation that closes a
/// sentence and a bracket that closes the prose around the URL are not part of it.
/// </summary>
public class BareUrlScannerTests
{
    [Fact]
    public void AnHttpsUrlInProse_IsFoundWhereItStands()
    {
        const string title = "Concurrency issue: https://claude.ai/artifact/UkBhdNFEcb95CNmbrJbzWr";

        var url = Assert.Single(BareUrlScanner.FindAll(title));

        Assert.Equal(19, url.Index);
        Assert.Equal("https://claude.ai/artifact/UkBhdNFEcb95CNmbrJbzWr", url.Url);
    }

    [Theory]
    [InlineData("http://example.com/a")]
    [InlineData("https://example.com/a")]
    [InlineData("HTTPS://Example.com/a")]
    [InlineData("http://localhost:5000/health")]
    public void AWebUrl_IsALink(string text)
    {
        var url = Assert.Single(BareUrlScanner.FindAll(text));

        Assert.Equal(text, url.Url);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript://example.com/%0aalert(1)")]
    [InlineData("data:text/html,<b>hi</b>")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("www.example.com")]
    [InlineData("see example.com/a")]
    public void AnythingButHttpOrHttps_IsNotALink(string text)
    {
        Assert.Empty(BareUrlScanner.FindAll(text));
    }

    [Theory]
    [InlineData("https://")]
    [InlineData("https:// example.com")]
    [InlineData("https://.")]
    [InlineData("https:///path")]
    public void ASchemeWithNoHost_IsNotALink(string text)
    {
        Assert.Empty(BareUrlScanner.FindAll(text));
    }

    [Fact]
    public void ASchemeInsideAWord_IsNotALink()
    {
        Assert.Empty(BareUrlScanner.FindAll("xhttps://example.com and 1http://example.com"));
    }

    [Theory]
    [InlineData("see https://example.com/a.")]
    [InlineData("see https://example.com/a,")]
    [InlineData("see https://example.com/a:")]
    [InlineData("see https://example.com/a;")]
    [InlineData("see https://example.com/a!")]
    [InlineData("see https://example.com/a?")]
    [InlineData("see https://example.com/a*")]
    [InlineData("see https://example.com/a_")]
    [InlineData("see https://example.com/a~")]
    [InlineData("see https://example.com/a'")]
    [InlineData("see https://example.com/a\"")]
    [InlineData("see https://example.com/a?!.")]
    public void PunctuationThatEndsTheSentence_IsNotPartOfTheUrl(string text)
    {
        var url = Assert.Single(BareUrlScanner.FindAll(text));

        Assert.Equal("https://example.com/a", url.Url);
    }

    [Fact]
    public void PunctuationInsideTheUrl_StaysInIt()
    {
        var url = Assert.Single(BareUrlScanner.FindAll("https://example.com/a.b?c=d,e;f&g=h!i"));

        Assert.Equal("https://example.com/a.b?c=d,e;f&g=h!i", url.Url);
    }

    [Fact]
    public void AClosingBracketThatClosesTheProse_StaysOutsideTheUrl()
    {
        var url = Assert.Single(BareUrlScanner.FindAll("(https://example.com/a)"));

        Assert.Equal(1, url.Index);
        Assert.Equal("https://example.com/a", url.Url);
    }

    [Fact]
    public void ABalancedBracketPair_IsPartOfTheUrl()
    {
        var url = Assert.Single(BareUrlScanner.FindAll("https://en.wikipedia.org/wiki/Foo_(bar)"));

        Assert.Equal("https://en.wikipedia.org/wiki/Foo_(bar)", url.Url);
    }

    [Fact]
    public void ABalancedPairInsideBracketedProse_KeepsOnlyItsOwnPair()
    {
        var url = Assert.Single(BareUrlScanner.FindAll("(see https://en.wikipedia.org/wiki/Foo_(bar))."));

        Assert.Equal("https://en.wikipedia.org/wiki/Foo_(bar)", url.Url);
    }

    [Fact]
    public void AHashOrAnAtInsideTheUrl_BelongsToTheUrl()
    {
        const string title = "Fix #deploy https://x.com/a#frag @bob";

        var url = Assert.Single(BareUrlScanner.FindAll(title));

        Assert.Equal(12, url.Index);
        Assert.Equal("https://x.com/a#frag", url.Url);
    }

    [Fact]
    public void AnAtInThePath_DoesNotEndTheUrl()
    {
        var url = Assert.Single(BareUrlScanner.FindAll("https://x.com/@bob/posts#top"));

        Assert.Equal("https://x.com/@bob/posts#top", url.Url);
    }

    [Theory]
    [InlineData("https://example.com/a b", "https://example.com/a")]
    [InlineData("https://example.com/a\tb", "https://example.com/a")]
    [InlineData("https://example.com/a<b>", "https://example.com/a")]
    [InlineData("https://example.com/a>b", "https://example.com/a")]
    [InlineData("Source: <https://example.com/es>", "https://example.com/es")]
    public void WhitespaceOrAnAngleBracket_EndsTheUrl(string text, string expected)
    {
        var url = Assert.Single(BareUrlScanner.FindAll(text));

        Assert.Equal(expected, url.Url);
    }

    [Fact]
    public void SeveralUrls_AreFoundInTheOrderTheyAppear()
    {
        var urls = BareUrlScanner.FindAll("first https://a.example/x then http://b.example/y and https://a.example/x");

        Assert.Equal(
            ["https://a.example/x", "http://b.example/y", "https://a.example/x"],
            urls.Select(url => url.Url));
    }

    [Fact]
    public void FindNext_StartsLookingWhereItIsTold()
    {
        const string text = "a https://one.example b https://two.example";

        var next = BareUrlScanner.FindNext(text, 3);

        Assert.Equal(24, next?.Index);
        Assert.Equal("https://two.example", next?.Url);
    }

    [Fact]
    public void FindNext_ReportsNothingPastTheLastUrl()
    {
        Assert.Null(BareUrlScanner.FindNext("a https://one.example b", 4));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No link in this title")]
    public void TextWithoutAUrl_HasNone(string? text)
    {
        Assert.Empty(BareUrlScanner.FindAll(text));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("HTTP://localhost:5000")]
    [InlineData("https://example.com/a.")]
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar))")]
    public void IsWebUrl_AcceptsAWholeWebUrl_TrailingCharactersIncluded(string url)
    {
        // The angle-bracket form says where the URL ends itself, so nothing is
        // trimmed; only the scheme and the host are asked about.
        Assert.True(BareUrlScanner.IsWebUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://")]
    [InlineData("https://.")]
    [InlineData("https:///path")]
    [InlineData("https:example.com")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("https://example.com/\u0007")]
    public void IsWebUrl_RejectsWhatABareUrlWouldNotLink(string? url)
    {
        Assert.False(BareUrlScanner.IsWebUrl(url));
    }

    [Fact]
    public void AFoundUrl_IsTheExactTextItWasReadFrom()
    {
        const string text = "go to (https://en.wikipedia.org/wiki/Foo_(bar)), then https://example.com/a.";

        foreach (var url in BareUrlScanner.FindAll(text))
        {
            Assert.Equal(url.Url, text.Substring(url.Index, url.Length));
        }
    }
}
