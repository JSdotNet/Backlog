using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The Drift at a glance part's figures: the sweeps' units grouped by the direction they
/// go, the drift issues still open, and the units whose drift issue carries
/// <c>sync-failed</c> — with either source able to answer alone.
/// </summary>
public class DriftInsightsTests
{
    private static readonly DashboardRepository Backlog = new("backlog", "JSdotNet/Backlog");

    private static readonly DashboardRepository Specs = new("specs", "JSdotNet/spec-manager");

    private static readonly DateTimeOffset Checked = new(2026, 10, 5, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Units_are_grouped_by_direction_push_pull_sync_report_off_then_unrecorded()
    {
        var units = new StubUnits(
            Unit(Backlog, ".devbook/domain/inbox/domain.md#capture", "report"),
            Unit(Backlog, ".devbook/domain/sessions/domain.md#delivery-run", "push"),
            Unit(Backlog, ".devbook/domain/inbox/features.md#quick-capture", "pull"),
            Unit(Backlog, ".devbook/domain/tasks/domain.md#task", null),
            Unit(Backlog, ".devbook/domain/devbook/domain.md#sync-verdict", "off"),
            Unit(Backlog, ".devbook/domain/roadmap/domain.md#plan", "SYNC"));

        var result = await Insights(units, new StubIssues()).GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(result.HasValue);
        Assert.Equal(["push", "pull", "sync", "report", "off", null], result.Value!.Directions.Select(direction => direction.Direction));
        Assert.Equal(6, result.Value.UnitCount);
    }

    [Fact]
    public async Task The_repository_chips_narrow_both_units_and_issues()
    {
        var units = new StubUnits(
            Unit(Backlog, ".devbook/domain/sessions/domain.md#delivery-run", "push"),
            Unit(Specs, ".devbook/domain/review/domain.md#proposal", "pull"));
        var issues = new StubIssues();

        var scope = DashboardScope.Default with { Repositories = RepositoryFocus.Of("specs") };
        var result = await Insights(units, issues).GetDriftAsync(scope, TestContext.Current.CancellationToken);

        var unit = Assert.Single(result.Value!.Directions.SelectMany(direction => direction.Units));
        Assert.Equal("specs", unit.RepositoryAlias);
        Assert.Equal(["specs"], Assert.Single(issues.Asked).Select(repository => repository.Alias));
    }

    [Fact]
    public async Task A_unit_whose_drift_issue_carries_sync_failed_is_marked()
    {
        var units = new StubUnits(
            Unit(Backlog, ".devbook/domain/sessions/domain.md#delivery-run", "push"),
            Unit(Backlog, ".devbook/domain/inbox/features.md#quick-capture", "pull"));
        var issues = new StubIssues(
            new DriftIssue("backlog", 912, "[Devbook drift] .devbook/domain/sessions/domain.md#Delivery-Run", "https://github.com/JSdotNet/Backlog/issues/912", SyncFailed: true),
            new DriftIssue("backlog", 913, "[Devbook drift] .devbook/domain/inbox/features.md#quick-capture", "https://github.com/JSdotNet/Backlog/issues/913", SyncFailed: false));

        var result = await Insights(units, issues).GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        var all = result.Value!.Directions.SelectMany(direction => direction.Units).ToList();
        Assert.True(all.Single(unit => unit.Direction == "push").SyncFailed);
        Assert.False(all.Single(unit => unit.Direction == "pull").SyncFailed);
        Assert.Equal(2, result.Value.Issues.Count);
        Assert.Equal(912, Assert.Single(result.Value.SyncFailed).Number);
        Assert.Null(result.Value.IssuesNote);
    }

    [Fact]
    public async Task Issues_that_cannot_be_read_leave_the_units_standing_and_say_why()
    {
        var units = new StubUnits(Unit(Backlog, ".devbook/domain/sessions/domain.md#delivery-run", "push"));
        var issues = new StubIssues { Availability = InsightAvailability.Unavailable("GitHub is not signed in.") };

        var result = await Insights(units, issues).GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(result.HasValue);
        Assert.Equal(1, result.Value!.UnitCount);
        Assert.Empty(result.Value.Issues);
        Assert.Contains("GitHub is not signed in.", result.Value.IssuesNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_incomplete_issue_read_is_a_floor_and_says_so()
    {
        var issues = new StubIssues(new DriftIssue("backlog", 1, "[Devbook drift] x", "u", false))
        {
            Complete = false,
            Failure = "API rate limit exceeded."
        };

        var result = await Insights(new StubUnits(), issues).GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Single(result.Value!.Issues);
        Assert.Equal(
            "Some repositories' drift issues could not be read (API rate limit exceeded), so the issues listed are a floor.",
            result.Value.IssuesNote);
    }

    [Fact]
    public async Task A_failing_issue_read_is_a_note_not_a_failed_part()
    {
        var units = new StubUnits(Unit(Backlog, ".devbook/domain/sessions/domain.md#delivery-run", "push"));
        var issues = new StubIssues { Thrown = new HttpRequestException("rate limited") };

        var result = await Insights(units, issues).GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(result.HasValue);
        Assert.Contains("rate limited", result.Value!.IssuesNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Neither_source_answering_makes_the_part_unavailable()
    {
        var units = new StubUnits { Available = false };
        var issues = new StubIssues { Availability = InsightAvailability.Unavailable("GitHub is not signed in.") };

        var result = await Insights(units, issues).GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.False(result.HasValue);
        Assert.Contains("keeps no devbook sync verdicts", result.Availability.Reason, StringComparison.Ordinal);
        Assert.Contains("GitHub is not signed in.", result.Availability.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sweep's verdict file and GitHub can spell an alias differently; the match is
    /// the alias, not its case (entry 19).
    /// </summary>
    [Fact]
    public async Task A_sync_failed_issue_marks_its_unit_whatever_the_aliases_case()
    {
        var units = new StubUnits(Unit(Backlog, ".devbook/domain/sessions/domain.md#delivery-run", "push"));
        var issues = new StubIssues(
            new DriftIssue("Backlog", 912, "[Devbook drift] .devbook/domain/sessions/domain.md#delivery-run", "https://github.com/JSdotNet/Backlog/issues/912", SyncFailed: true));

        var result = await new DriftInsights(new Directory(new DashboardRepository("Backlog", "JSdotNet/Backlog")), new LooseUnits(units, "backlog"), issues)
            .GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Value!.Directions.SelectMany(direction => direction.Units)).SyncFailed);
    }

    /// <summary>
    /// Entry 21: the Overview tile, the Devbook tab and Needs you each asked for drift as
    /// the pane opened, and each went to GitHub once per repository. Within a scope the
    /// issues are read once and every later ask is answered from that read.
    /// </summary>
    [Fact]
    public async Task Two_asks_within_a_scope_read_the_issues_once()
    {
        var issues = new StubIssues(new DriftIssue("backlog", 1, "[Devbook drift] x", "u", false));
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        var second = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Single(issues.Asked);
        Assert.Single(second.Value!.Issues);
    }

    [Fact]
    public async Task Asks_running_side_by_side_share_one_read()
    {
        var issues = new StubIssues(new DriftIssue("backlog", 1, "[Devbook drift] x", "u", false));
        var insights = Insights(new StubUnits(), issues);

        await Task.WhenAll(
            insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken),
            insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken),
            insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken));

        Assert.Single(issues.Asked);
    }

    /// <summary>The window and the machine do not reach drift, so moving either is not a
    /// new question for GitHub.</summary>
    [Fact]
    public async Task Moving_the_window_or_the_machine_does_not_read_the_issues_again()
    {
        var issues = new StubIssues();
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetDriftAsync(DashboardScope.Default with { Period = DashboardPeriod.FourWeeks, MachineId = "tower" }, TestContext.Current.CancellationToken);

        Assert.Single(issues.Asked);
    }

    [Fact]
    public async Task A_different_repository_focus_is_a_read_of_its_own()
    {
        var issues = new StubIssues();
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetDriftAsync(new DashboardScope(RepositoryFocus.Of("specs")), TestContext.Current.CancellationToken);

        Assert.Equal(2, issues.Asked.Count);
    }

    /// <summary>A read that failed is not kept (review, Batch A): GitHub signed in or the
    /// network back is seen on the next ask, not only after a refresh.</summary>
    [Fact]
    public async Task A_failed_issue_read_is_asked_again()
    {
        var issues = new StubIssues { Thrown = new HttpRequestException("No route to host.") };
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, issues.Asked.Count);
    }

    [Fact]
    public async Task An_issue_source_that_cannot_answer_is_asked_again()
    {
        var issues = new StubIssues { Availability = InsightAvailability.Unavailable("GitHub is not signed in.") };
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, issues.AvailabilityAsked);
    }

    /// <summary>A rate-limit refusal is kept: asking again within seconds only spends
    /// the limit that refused it.</summary>
    [Fact]
    public async Task A_rate_limited_read_is_kept_until_a_refresh()
    {
        var issues = new StubIssues { Complete = false, Failure = "gh: API rate limit exceeded for user ID 7." };
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Single(issues.Asked);
    }

    [Fact]
    public async Task A_partial_read_for_another_reason_is_asked_again()
    {
        var issues = new StubIssues { Complete = false, Failure = "JSdotNet/finance: gh: Not Found (HTTP 404)" };
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, issues.Asked.Count);
    }

    [Fact]
    public async Task Invalidating_goes_back_to_GitHub()
    {
        var issues = new StubIssues();
        var insights = Insights(new StubUnits(), issues);

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        insights.Invalidate();
        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(2, issues.Asked.Count);
    }

    /// <summary>The verdicts are a local file and cheap; only the GitHub read is kept, so a
    /// sweep that lands between two asks shows on the second.</summary>
    [Fact]
    public async Task The_verdicts_are_read_afresh_on_every_ask()
    {
        var units = new CountingUnits();
        var insights = Insights(units, new StubIssues());

        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);
        _ = await insights.GetDriftAsync(DashboardScope.Default, TestContext.Current.CancellationToken);

        Assert.Equal(4, units.Reads);
    }

    [Theory]
    [InlineData("gh: API rate limit exceeded for user ID 7. If you reach out to GitHub Support, include the request ID. (HTTP 403)", "gh: API rate limit exceeded for user ID 7.")]
    [InlineData("gh: Validation Failed (HTTP 422)", "gh: Validation Failed (HTTP 422)")]
    public void A_providers_message_is_cut_to_its_first_sentence(string message, string reason) =>
        Assert.Equal(reason, DriftInsights.Reason(message));

    [Theory]
    [InlineData("[Devbook drift] .devbook/domain/a.md#B", ".devbook/domain/a.md#B")]
    [InlineData("  [devbook drift]   x#y  ", "x#y")]
    [InlineData("Something else", "Something else")]
    public void An_issue_names_its_subject_as_the_title_spells_it(string title, string subject) =>
        Assert.Equal(subject, new DriftIssue("backlog", 1, title, "u", false).Subject);

    [Theory]
    [InlineData("[Devbook drift] .devbook/domain/a.md#B", ".devbook/domain/a.md#b")]
    [InlineData("  [devbook drift]   x#y  ", "x#y")]
    [InlineData("Something else", "something else")]
    public void A_drift_issue_title_names_its_unit(string title, string reference) =>
        Assert.Equal(reference, DriftInsights.Reference(title));

    private static DriftUnit Unit(DashboardRepository repository, string unit, string? direction) =>
        new(repository.Alias, unit, "aggregate", direction, null, "code-ahead", "pr", null, Checked);

    private static DriftInsights Insights(IDriftUnitSource units, IDriftIssueSource issues) =>
        new(new Directory(Backlog, Specs), units, issues);

    private sealed class Directory(params DashboardRepository[] repositories) : IRepositoryDirectory
    {
        public IReadOnlyList<DashboardRepository> Repositories => repositories;
    }

    private sealed class StubUnits(params DriftUnit[] units) : IDriftUnitSource
    {
        public bool Available { get; init; } = true;

        public bool IsAvailable => Available;

        public IReadOnlyList<DriftUnit> Units(DashboardRepository repository) =>
            [.. units.Where(unit => unit.RepositoryAlias == repository.Alias)];
    }

    /// <summary>Answers for a repository whatever alias the verdict file spelled it with.</summary>
    private sealed class LooseUnits(StubUnits inner, string spelledAs) : IDriftUnitSource
    {
        public bool IsAvailable => inner.IsAvailable;

        public IReadOnlyList<DriftUnit> Units(DashboardRepository repository) =>
            inner.Units(repository with { Alias = spelledAs });
    }

    private sealed class CountingUnits : IDriftUnitSource
    {
        public int Reads { get; private set; }

        public bool IsAvailable => true;

        public IReadOnlyList<DriftUnit> Units(DashboardRepository repository)
        {
            Reads++;
            return [];
        }
    }

    private sealed class StubIssues(params DriftIssue[] issues) : IDriftIssueSource
    {
        public InsightAvailability Availability { get; init; } = InsightAvailability.Available;

        public bool Complete { get; init; } = true;

        public string? Failure { get; init; }

        public Exception? Thrown { get; init; }

        public List<IReadOnlyList<DashboardRepository>> Asked { get; } = [];

        public int AvailabilityAsked { get; private set; }

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            AvailabilityAsked++;
            return Task.FromResult(Availability);
        }

        public Task<DriftIssueRead> GetOpenAsync(
            IReadOnlyList<DashboardRepository> repositories,
            CancellationToken cancellationToken = default)
        {
            lock (Asked) Asked.Add(repositories);

            if (Thrown is not null) return Task.FromException<DriftIssueRead>(Thrown);

            var aliases = repositories.Select(repository => repository.Alias).ToHashSet();

            return Task.FromResult(new DriftIssueRead([.. issues.Where(issue => aliases.Contains(issue.RepositoryAlias))], Complete, Failure));
        }
    }
}
