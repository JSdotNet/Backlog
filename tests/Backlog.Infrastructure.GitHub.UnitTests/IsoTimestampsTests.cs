using System.Globalization;
using System.Text.Json;
using Backlog.Infrastructure;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// GitHub and Anthropic answer with ISO-8601 timestamps, and the query strings
/// sent to them carry RFC 3339 instants. Both have to mean the same instant on a
/// machine whose culture is not the invariant one — a Thai Buddhist calendar
/// reads "2026" as a Buddhist-era year, a German one is what most of the team
/// runs, and Windows lets a person change their own date and time separators — a
/// "-" between hours and minutes is enough to make a culture-sensitive parse of
/// GitHub's "05:06:07" fail outright.
/// </summary>
public sealed class IsoTimestampsTests
{
    private static readonly DateTimeOffset Expected = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>A German culture whose time separator a person set to "-" in
    /// Windows' regional settings.</summary>
    private const string CustomisedGerman = "de-DE with a '-' time separator";

    public static TheoryData<string> Cultures => ["de-DE", "th-TH", "nl-NL", CustomisedGerman];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_timestamp_reads_as_the_same_instant_under_any_culture(string culture)
    {
        using var json = JsonDocument.Parse("""{ "at": "2026-03-04T05:06:07Z" }""");

        var parsed = UnderCulture(culture, () => IsoTimestamps.Timestamp(json.RootElement, "at"));

        Assert.Equal(Expected, parsed);
    }

    [Theory]
    [InlineData("""{ "other": "2026-03-04T05:06:07Z" }""")]
    [InlineData("""{ "at": null }""")]
    [InlineData("""{ "at": 12345 }""")]
    [InlineData("""{ "at": "not a timestamp" }""")]
    [InlineData("""[ "2026-03-04T05:06:07Z" ]""")]
    [InlineData("""  "2026-03-04T05:06:07Z" """)]
    public void A_missing_or_unreadable_timestamp_is_null(string payload)
    {
        using var json = JsonDocument.Parse(payload);

        Assert.Null(IsoTimestamps.Timestamp(json.RootElement, "at"));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void An_instant_formats_as_utc_rfc3339_under_any_culture(string culture)
    {
        var instant = new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2));

        var formatted = UnderCulture(culture, () => IsoTimestamps.Rfc3339(instant));

        Assert.Equal("2026-03-04T05:06:07Z", formatted);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void An_issue_keeps_its_updated_at_under_any_culture(string culture)
    {
        using var json = JsonDocument.Parse("""
            { "number": 723, "html_url": "https://github.com/JSdotNet/Backlog/issues/723",
              "title": "Parse timestamps", "state": "open", "updated_at": "2026-03-04T05:06:07Z" }
            """);

        var issue = UnderCulture(culture, () => GitHubClient.ReadIssue(json.RootElement));

        Assert.Equal(Expected, issue.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_merged_pull_request_reads_as_merged_under_any_culture(string culture)
    {
        using var json = JsonDocument.Parse("""
            { "number": 708, "state": "closed", "merged_at": "2026-03-04T05:06:07Z", "draft": false }
            """);

        var pull = UnderCulture(culture, () => GitHubClient.ReadPullRequest(json.RootElement, "JSdotNet/Backlog"));

        Assert.Equal(GitHubItemState.Merged, pull.State);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_merged_linked_pull_request_reads_as_merged_under_any_culture(string culture)
    {
        using var json = JsonDocument.Parse("""
            [ { "event": "cross-referenced",
                "source": { "issue": {
                  "number": 708, "state": "closed",
                  "html_url": "https://github.com/JSdotNet/Backlog/pull/708",
                  "repository": { "full_name": "JSdotNet/Backlog" },
                  "pull_request": { "merged_at": "2026-03-04T05:06:07Z" } } } } ]
            """);

        var pulls = UnderCulture(culture, () => GitHubClient.ReadLinkedPullRequests(json.RootElement));

        Assert.Equal(GitHubItemState.Merged, Assert.Single(pulls).State);
    }

    private static T UnderCulture<T>(string name, Func<T> read)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = Culture(name);
            CultureInfo.CurrentUICulture = Culture(name);
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static CultureInfo Culture(string name)
    {
        if (name != CustomisedGerman) return new CultureInfo(name);

        var culture = (CultureInfo)new CultureInfo("de-DE").Clone();
        culture.DateTimeFormat.TimeSeparator = "-";
        return culture;
    }
}
