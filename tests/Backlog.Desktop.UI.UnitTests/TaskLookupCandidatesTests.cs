using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The window's task rows as the Tasks lookup matches over them
/// (<see cref="ITaskLookupCandidates"/>, local ADR 0042 V8): plain values read off each
/// row — its id and title, the pull requests and sessions it linked, the GitHub issue
/// or display key of the task it is linked to, and whether it counts as mine — built
/// once and handed back as the same list until the Tasks state changes.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TaskLookupCandidatesTests
{
    private const string Repository = "JSdotNet/Backlog";

    private const string Entry = "# Wire the In progress view\n`prompt` `!in-progress` `repo:JSdotNet/Backlog`\n";

    [Fact]
    public async Task A_row_is_a_candidate_with_its_links_its_linked_issue_and_its_display_key()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        row.PullRequestLinks = [new EntryPullRequestLink(Repository, 712)];
        row.SessionLinks = [new EntrySessionLink(Repository, "session-1")];
        row.SourceRef = new SourceRef(
            "github", Repository, "issue-42", $"https://github.com/{Repository}/issues/42", "#42",
            assignee: null, sourceState: "open", sourceUpdatedAt: DateTimeOffset.UnixEpoch);
        host.State.SetSelectionMode(true);

        var candidate = Assert.Single(Candidates(host));

        Assert.Equal(row.Id, candidate.EntryId);
        Assert.Equal("Wire the In progress view", candidate.Title);
        Assert.Equal([new TaskPullRequestLink(Repository, 712)], candidate.PullRequests);
        Assert.Equal(new TaskIssueReference(Repository, 42), candidate.LinkedIssue);
        Assert.Equal("#42", candidate.DisplayKey);
        Assert.Equal(["session-1"], candidate.SessionIds);
        Assert.True(candidate.IsMine);
    }

    /// <summary>A task that is done or archived still leads to its pull requests, but no
    /// longer counts toward Mine.</summary>
    [Fact]
    public async Task A_done_task_is_not_mine()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        await host.WriteEntryAsync("# Shipped\n`prompt` `!done` `repo:JSdotNet/Backlog`\n");

        Assert.False(Assert.Single(Candidates(host)).IsMine);
    }

    /// <summary>The lookup asks once per drawn cell, so the list is built once; and any
    /// change the Tasks state announces builds it again, because a row's links, status
    /// or source can change in place where the list's identity would not show it.</summary>
    [Fact]
    public async Task The_same_list_comes_back_until_the_tasks_state_changes()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);

        var first = Candidates(host);
        Assert.Same(first, Candidates(host));
        Assert.Empty(Assert.Single(first).PullRequests);

        row.PullRequestLinks = [new EntryPullRequestLink(Repository, 712)];
        host.State.SetSelectionMode(true);

        var second = Candidates(host);
        Assert.NotSame(first, second);
        Assert.Equal([new TaskPullRequestLink(Repository, 712)], Assert.Single(second).PullRequests);
    }

    [Theory]
    [InlineData("github", "JSdotNet/Backlog", "#42", "https://github.com/JSdotNet/Backlog/issues/42", 42)]
    [InlineData("github", "JSdotNet/Backlog", "", "https://github.com/JSdotNet/Backlog/issues/43", 43)]
    [InlineData("spec-manager", "fin", "#42 · FIN-1", "https://example.com/42", null)]
    public void A_github_linked_task_names_its_issue(string connector, string target, string displayKey, string url, int? expected)
    {
        var issue = TaskLookupCandidates.GitHubIssueOf(connector, target, displayKey, url);

        Assert.Equal(expected, issue?.Number);
        if (expected is not null) Assert.Equal(target, issue!.RepositoryFullName);
    }

    private static IReadOnlyList<TaskLookupCandidate> Candidates(TasksPaneHost host) =>
        ((ITaskLookupCandidates)host.State).Candidates;
}
