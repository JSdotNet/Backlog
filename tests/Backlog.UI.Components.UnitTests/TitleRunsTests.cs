namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The title splitter every task surface draws its title through: prose, tags and
/// bare web URLs, in order, concatenating back to the title character for
/// character.
/// </summary>
public sealed class TitleRunsTests
{
    private const string ArtifactUrl = "https://claude.ai/artifact/UkBhdNFEcb95CNmbrJbzWr";

    [Fact]
    public void A_url_in_a_title_is_its_own_run()
    {
        var runs = TitleRuns.Split($"Concurrency issue: {ArtifactUrl}");

        Assert.Equal(
            [new TitleRun("Concurrency issue: ", TitleRunKind.Text), new TitleRun(ArtifactUrl, TitleRunKind.Url)],
            runs);
    }

    [Fact]
    public void A_title_with_no_url_splits_exactly_as_its_tags_do()
    {
        var runs = TitleRuns.Split("Ship the installer @bob #deploy");

        Assert.Equal(
            [
                new TitleRun("Ship the installer ", TitleRunKind.Text),
                new TitleRun("@bob", TitleRunKind.Tag),
                new TitleRun(" ", TitleRunKind.Text),
                new TitleRun("#deploy", TitleRunKind.Tag)
            ],
            runs);
    }

    [Fact]
    public void A_sigil_inside_a_url_never_opens_a_tag()
    {
        var runs = TitleRuns.Split("Fix #deploy https://x.com/a#frag @bob");

        Assert.Equal(
            [
                new TitleRun("Fix ", TitleRunKind.Text),
                new TitleRun("#deploy", TitleRunKind.Tag),
                new TitleRun(" ", TitleRunKind.Text),
                new TitleRun("https://x.com/a#frag", TitleRunKind.Url),
                new TitleRun(" ", TitleRunKind.Text),
                new TitleRun("@bob", TitleRunKind.Tag)
            ],
            runs);
    }

    [Fact]
    public void A_url_whose_path_holds_an_at_or_a_plus_stays_one_run()
    {
        var runs = TitleRuns.Split("Read https://x.com/@bob/+plan/#top");

        Assert.Equal(
            [new TitleRun("Read ", TitleRunKind.Text), new TitleRun("https://x.com/@bob/+plan/#top", TitleRunKind.Url)],
            runs);
    }

    [Fact]
    public void Sentence_punctuation_after_a_url_stays_text()
    {
        var runs = TitleRuns.Split("see https://example.com/a.");

        Assert.Equal(
            [
                new TitleRun("see ", TitleRunKind.Text),
                new TitleRun("https://example.com/a", TitleRunKind.Url),
                new TitleRun(".", TitleRunKind.Text)
            ],
            runs);
    }

    [Fact]
    public void A_closing_parenthesis_the_url_did_not_open_stays_text()
    {
        var runs = TitleRuns.Split("(https://example.com/a)");

        Assert.Equal(
            [
                new TitleRun("(", TitleRunKind.Text),
                new TitleRun("https://example.com/a", TitleRunKind.Url),
                new TitleRun(")", TitleRunKind.Text)
            ],
            runs);
    }

    [Theory]
    [InlineData("Run javascript:alert(1) now")]
    [InlineData("Embed data:text/html;base64,PGI+ now")]
    [InlineData("Fetch ftp://files.example.com/a now")]
    [InlineData("Mail mailto:bob@example.com now")]
    [InlineData("Visit www.example.com now")]
    public void Only_http_and_https_become_url_runs(string title)
    {
        var runs = TitleRuns.Split(title);

        Assert.DoesNotContain(runs, run => run.Kind == TitleRunKind.Url);
        Assert.Empty(TitleRuns.Urls(title));
    }

    [Theory]
    [InlineData("Concurrency issue: https://claude.ai/artifact/UkBhdNFEcb95CNmbrJbzWr")]
    [InlineData("Fix #deploy https://x.com/a#frag @bob")]
    [InlineData("(see https://en.wikipedia.org/wiki/Foo_(bar)).")]
    [InlineData("Two: https://a.example/x and https://b.example/y, done")]
    [InlineData("#https://x.com")]
    [InlineData("Source: <https://example.com/es>")]
    [InlineData("   ")]
    [InlineData("Plain words only")]
    public void The_runs_concatenate_back_to_the_title(string title)
    {
        var runs = TitleRuns.Split(title);

        Assert.Equal(title, string.Concat(runs.Select(run => run.Text)));
    }

    [Fact]
    public void A_tag_cut_through_by_a_url_reads_as_text_and_neighbouring_text_runs_merge()
    {
        // "#https" would be a tag on its own, but the URL claims the letters; the
        // sigil left behind is prose, not a chip with nothing after it.
        var runs = TitleRuns.Split("See #https://x.com");

        Assert.Equal(
            [new TitleRun("See #", TitleRunKind.Text), new TitleRun("https://x.com", TitleRunKind.Url)],
            runs);
    }

    [Fact]
    public void Two_distinct_urls_are_offered_in_title_order()
    {
        var urls = TitleRuns.Urls("Compare https://b.example/y with https://a.example/x");

        Assert.Equal(["https://b.example/y", "https://a.example/x"], urls);
    }

    [Fact]
    public void The_same_url_twice_is_offered_once()
    {
        var urls = TitleRuns.Urls("https://a.example/x then https://a.example/x again");

        Assert.Equal(["https://a.example/x"], urls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_title_has_no_runs_and_no_urls(string? title)
    {
        Assert.Empty(TitleRuns.Split(title));
        Assert.Empty(TitleRuns.Urls(title));
    }

    [Theory]
    [InlineData("https://claude.ai/artifact/UkBhdNFEcb95CNmbrJbzWr", "claude.ai")]
    [InlineData("http://localhost:5000/a", "localhost")]
    [InlineData("https://GitHub.com/JSdotNet/Backlog/pull/1", "github.com")]
    public void A_link_is_labelled_by_its_host(string url, string host)
    {
        Assert.Equal(host, TitleRuns.Host(url));
    }
}
