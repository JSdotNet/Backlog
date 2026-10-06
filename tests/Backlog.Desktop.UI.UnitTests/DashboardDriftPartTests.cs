using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Dashboard.UI.Parts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The dashboard's Drift at a glance part: the devbook sync units by direction, each with
/// its last verdict, the open drift issues, and the sync-failed ones called out.
/// <para>
/// Through the module's own derivation over scripted ports, so what is asserted is what a
/// host shows.
/// </para>
/// </summary>
public sealed class DashboardDriftPartTests
{
    private static readonly DateTimeOffset Checked = new(2026, 10, 5, 4, 30, 0, TimeSpan.Zero);

    private static readonly DriftUnit Sessions = new(
        "backlog", ".devbook/domain/sessions/domain.md#delivery-run", "aggregate", "push",
        ".devbook/domain/sessions/context.md", "spec-ahead", "pr", "https://github.com/JSdotNet/Backlog/pull/950", Checked);

    private static readonly DriftUnit Inbox = new(
        "backlog", ".devbook/domain/inbox/features.md#quick-capture", "feature", "pull",
        ".devbook/domain/inbox/context.md", "code-ahead", "failed", null, Checked);

    private static readonly DriftUnit Tasks = new(
        "backlog", ".devbook/domain/tasks/domain.md#task", "aggregate", "report",
        null, "aligned", "none", null, Checked);

    private static readonly DriftIssue InboxIssue = new(
        "backlog", 913, "[Devbook drift] .devbook/domain/inbox/features.md#quick-capture",
        "https://github.com/JSdotNet/Backlog/issues/913", SyncFailed: true);

    private static readonly DriftIssue OtherIssue = new(
        "backlog", 914, "[Devbook drift] .devbook/domain/tasks/domain.md#task-list",
        "https://github.com/JSdotNet/Backlog/issues/914", SyncFailed: false);

    [Fact]
    public void Units_read_grouped_by_direction_with_their_last_verdict()
    {
        using var context = Context(new ScriptedUnits(Sessions, Inbox, Tasks), new ScriptedIssues(OtherIssue, InboxIssue));

        var part = context.Render<DriftPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Equal("Drift at a glance", part.Find("[data-testid='dashboard-drift'] .dashboard-part__title").TextContent);

        var table = part.Find("[data-testid='dashboard-drift-units-table']");
        Assert.Equal(
            ["push — chapter to code", "pull — code to chapter", "report — verdicts only"],
            table.QuerySelectorAll(".data-table__group-name").Select(name => name.TextContent));

        var verdicts = part.FindAll("[data-testid='dashboard-drift-unit-verdict']");
        Assert.Equal(["spec-ahead", "code-ahead", "aligned"], verdicts.Select(badge => badge.TextContent));
        Assert.Contains("badge--sync-verdict-spec-ahead", verdicts[0].ClassName, StringComparison.Ordinal);

        // The badge leads to what the sweep opened, where there is something.
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/950", verdicts[0].GetAttribute("href"));
        Assert.Equal("SPAN", verdicts[2].TagName);

        // The shared folder is dropped from the label and kept in the title.
        var first = table.QuerySelector("tbody tr:not(.data-table__group) td")!;
        Assert.StartsWith("domain/sessions/domain.md#delivery-run", first.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal(Sessions.Unit, first.GetAttribute("title"));

        Assert.Contains("3", part.Find("[data-testid='dashboard-drift-units']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_unit_whose_drift_issue_carries_sync_failed_says_so_and_the_issue_leads()
    {
        using var context = Context(new ScriptedUnits(Sessions, Inbox, Tasks), new ScriptedIssues(OtherIssue, InboxIssue));

        var part = context.Render<DriftPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var failed = Assert.Single(part.FindAll("[data-testid='dashboard-drift-unit-failed']"));
        Assert.Contains("dashboard-drift__unit--failed", failed.Closest("tr")!.ClassName, StringComparison.Ordinal);

        Assert.Contains("2", part.Find("[data-testid='dashboard-drift-issues-open']").TextContent, StringComparison.Ordinal);
        Assert.Contains("1", part.Find("[data-testid='dashboard-drift-sync-failed']").TextContent, StringComparison.Ordinal);

        // Sync-failed first: those are the ones waiting on a person.
        var links = part.FindAll("[data-testid='dashboard-drift-issue-link']");
        Assert.Equal(["https://github.com/JSdotNet/Backlog/issues/913", "https://github.com/JSdotNet/Backlog/issues/914"], links.Select(link => link.GetAttribute("href")));
        Assert.Single(part.FindAll("[data-testid='dashboard-drift-issue-failed']"));
    }

    [Fact]
    public void Issues_github_cannot_read_show_a_dash_not_a_zero_and_the_units_stay()
    {
        using var context = Context(new ScriptedUnits(Sessions), new ScriptedIssues { Availability = InsightAvailability.Unavailable("GitHub is not signed in.") });

        var part = context.Render<DriftPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        Assert.Single(part.FindAll("[data-testid='dashboard-drift-unit-verdict']"));
        Assert.Contains("—", part.Find("[data-testid='dashboard-drift-issues-open']").TextContent, StringComparison.Ordinal);
        // One sentence, the reason, rather than a heading and a note disagreeing about how
        // much was read.
        var table = part.Find("[data-testid='dashboard-drift-issues-table']").TextContent;
        Assert.Contains("The drift issues could not be read: GitHub is not signed in.", table, StringComparison.Ordinal);
        Assert.DoesNotContain("Some repositories", table, StringComparison.Ordinal);

        // Said once: the tile keeps its dash and leaves the reason to the table.
        Assert.DoesNotContain("GitHub is not signed in.", part.Find("[data-testid='dashboard-drift-issues-open']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_read_says_the_count_is_a_floor_in_plain_sight_under_the_tile()
    {
        using var context = Context(new ScriptedUnits(Sessions), new ScriptedIssues(OtherIssue) { Complete = false, Failure = "gh: Validation Failed (HTTP 422)" });

        var part = context.Render<DriftPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var tile = part.Find("[data-testid='dashboard-drift-issues-open']");
        Assert.Contains("so the issues listed are a floor", tile.TextContent, StringComparison.Ordinal);
        Assert.Empty(tile.QuerySelectorAll(".info-hint__trigger"));
        Assert.Single(part.FindAll("[data-testid='dashboard-drift-issue-link']"));
    }

    [Fact]
    public void Neither_source_answering_is_unavailable_with_the_reason()
    {
        using var context = Context(new ScriptedUnits { Available = false }, new ScriptedIssues { Availability = InsightAvailability.Unavailable("GitHub is not signed in.") });

        var part = context.Render<DriftPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));

        var status = part.Find("[data-testid='dashboard-drift-status']");
        Assert.Contains("keeps no devbook sync verdicts", status.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_asks_nothing_new_and_the_repository_chips_do()
    {
        var issues = new ScriptedIssues();
        using var context = Context(new ScriptedUnits(Sessions), issues);

        var part = context.Render<DriftPart>(parameters => parameters.Add(p => p.Scope, DashboardScope.Default));
        Assert.Single(issues.Asked);

        part.Render(parameters => parameters.Add(p => p.Scope, DashboardScope.Default with { Period = DashboardPeriod.FourWeeks }));
        Assert.Single(issues.Asked);

        part.Render(parameters => parameters.Add(p => p.Scope, DashboardScope.Default with { Repositories = RepositoryFocus.Of("backlog") }));
        Assert.Equal(2, issues.Asked.Count);
    }

    [Fact]
    public void The_part_sits_in_the_dashboards_devbook_section()
    {
        using var context = new BunitContext();
        _ = context.Services.AddUnavailableDashboard("backlog");

        var pane = context.Render<DashboardPane>();

        var part = pane.Find("[data-testid='dashboard-devbook-section'] [data-testid='dashboard-drift']");
        Assert.Contains(DashboardTestHost.UnavailableReason, part.TextContent, StringComparison.Ordinal);
    }

    private static BunitContext Context(IDriftUnitSource units, IDriftIssueSource issues)
    {
        var context = new BunitContext();

        _ = context.Services.AddUnavailableDashboard("backlog");
        context.Services.AddSingleton(units);
        context.Services.AddSingleton(issues);
        context.Services.AddDashboardModule();

        return context;
    }

    private sealed class ScriptedUnits(params DriftUnit[] units) : IDriftUnitSource
    {
        public bool Available { get; init; } = true;

        public bool IsAvailable => Available;

        public IReadOnlyList<DriftUnit> Units(DashboardRepository repository) =>
            [.. units.Where(unit => unit.RepositoryAlias == repository.Alias)];
    }

    private sealed class ScriptedIssues(params DriftIssue[] issues) : IDriftIssueSource
    {
        public InsightAvailability Availability { get; init; } = InsightAvailability.Available;

        public bool Complete { get; init; } = true;

        public string? Failure { get; init; }

        public List<IReadOnlyList<DashboardRepository>> Asked { get; } = [];

        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Availability);

        public Task<DriftIssueRead> GetOpenAsync(
            IReadOnlyList<DashboardRepository> repositories,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(repositories);

            return Task.FromResult(new DriftIssueRead(issues, Complete, Failure));
        }
    }
}
