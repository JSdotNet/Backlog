using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What an entry shows of the work done on it: the pull requests a session linked
/// with <c>link_change</c>, as GitHub links, and the sessions that recorded
/// themselves with <c>link_session</c>, as buttons that open the session.
/// <para>
/// The projections are read by <see cref="EntryLinks"/>, which is asserted here on
/// its own. The pane is asserted with the row's links set directly: the mapping
/// from entry to row is one line in <c>RefreshRowFromEntry</c>, and what the pane
/// owns is how a link is drawn and what pressing it does.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class EntryWorkLinksTests
{
    private const string Entry = "# Record the adoption decision\n`prompt` `!in-progress`\n\nWrite the ADR.\n";

    [Fact]
    public void Pull_requests_are_read_from_their_own_projections_once_each()
    {
        var entry = Dto(
            new EntryProjectionDto("JSdotNet/Backlog", "42", EntryProjectionDto.IssueTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "655", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "655", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "not-a-number", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("backlog", "7", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "abc", EntryProjectionDto.SessionTargetType));

        var pr = Assert.Single(EntryLinks.PullRequests(entry));

        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/655", pr.Url);
        Assert.Equal("PR #655", pr.Label);
    }

    [Fact]
    public void Sessions_are_read_from_their_own_projections_once_each()
    {
        var entry = Dto(
            new EntryProjectionDto("JSdotNet/Backlog", "e711d47d-3e09-4254", EntryProjectionDto.SessionTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "E711D47D-3E09-4254", EntryProjectionDto.SessionTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "second", EntryProjectionDto.SessionTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "655", EntryProjectionDto.PullRequestTargetType));

        var sessions = EntryLinks.Sessions(entry);

        Assert.Equal(["e711d47d-3e09-4254", "second"], sessions.Select(s => s.SessionId));
        Assert.Equal("e711d47d", sessions[0].ShortId);
        Assert.Equal("second", sessions[1].ShortId);
        Assert.Equal("qa-no-su", new EntrySessionLink("JSdotNet/Backlog", "qa-no-such-session").ShortId);
    }

    /// <summary>A session is no issue: the pane's offer to file one survives it.</summary>
    [Fact]
    public void A_session_is_not_the_issue_an_entry_was_pushed_to()
    {
        var entry = Dto(new EntryProjectionDto("JSdotNet/Backlog", "123", EntryProjectionDto.SessionTargetType));

        Assert.Null(TasksIssues.FindLink(entry));
    }

    [Fact]
    public async Task The_entry_links_its_pull_request_and_opens_its_session()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 655)];
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "e711d47d-3e09-4254")];

        string? opened = null;
        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.OnOpenSession, (string id) => opened = id));

        var pr = pane.Find("[data-testid='entry-pull-request']");
        Assert.Equal("a", pr.TagName, ignoreCase: true);
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/655", pr.GetAttribute("href"));
        Assert.Contains("PR #655", pr.TextContent, StringComparison.Ordinal);

        var session = pane.Find("[data-testid='entry-session']");
        Assert.Equal("button", session.TagName, ignoreCase: true);
        Assert.Contains("Session e711d47d", session.TextContent, StringComparison.Ordinal);

        await session.ClickAsync(new());

        Assert.Equal("e711d47d-3e09-4254", opened);
    }

    /// <summary>With nobody to open it, a session is text, not a button that has
    /// to refuse the click.</summary>
    [Fact]
    public async Task A_session_nobody_can_open_is_inert_text()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "e711d47d")];

        var pane = host.Render();

        Assert.Equal("span", pane.Find("[data-testid='entry-session']").TagName, ignoreCase: true);
    }

    [Fact]
    public async Task An_entry_with_no_work_recorded_shows_neither()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-session']"));
    }

    private static TaskItemDto Dto(params EntryProjectionDto[] projections) => new(
        Guid.NewGuid(),
        "Record the adoption decision",
        string.Empty,
        EntryType.Task,
        Priority.Medium,
        EntryStatus.InProgress,
        Area: null,
        Tags: [],
        Order: 0,
        TotalSubItems: 0,
        CompletedSubItems: 0,
        Projections: projections);
}
