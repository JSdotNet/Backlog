using Backlog.Desktop.UI.InProgress;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// "Link a session or pull request…" taking a pull request by number or link, whatever
/// its state: the work that merged before its task was linked is the work a task a
/// goal holds counts as done by, so a merged or closed pull request is linked as an open
/// one is — read by number first, so one GitHub does not know is refused rather than
/// recorded.
/// <para>
/// bUnit swallows an exception thrown from a handler, so every link is asserted by its
/// effect — the link on the entry, the field's error, the toast.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class LinkPullRequestByReferenceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private const string Entry = "# Wire the In progress view\n`prompt` `!in-progress` `repo:JSdotNet/Backlog`\n";

    private const string Repository = "JSdotNet/Backlog";

    [Theory]
    [InlineData("1214", null, 1214)]
    [InlineData(" #1214 ", null, 1214)]
    [InlineData("JSdotNet/Backlog#1214", "JSdotNet/Backlog", 1214)]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/1214", "JSdotNet/Backlog", 1214)]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/1214/files", "JSdotNet/Backlog", 1214)]
    [InlineData("github.com/JSdotNet/Backlog/pull/1214?w=1", "JSdotNet/Backlog", 1214)]
    [InlineData("https://www.github.com/JSdotNet/Backlog/pull/1214#discussion", "JSdotNet/Backlog", 1214)]
    public void A_number_or_a_link_names_the_pull_request(string reference, string? repository, int number)
    {
        Assert.True(WorkLinker.TryParse(reference, out var parsedRepository, out var parsedNumber));
        Assert.Equal(repository, parsedRepository);
        Assert.Equal(number, parsedNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("Backlog#12")]
    [InlineData("JSdotNet/Backlog#")]
    [InlineData("https://github.com/JSdotNet/Backlog/issues/1214")]
    [InlineData("https://gitlab.com/JSdotNet/Backlog/pull/1214")]
    public void Anything_else_names_none(string reference)
    {
        Assert.False(WorkLinker.TryParse(reference, out var repository, out var number));
        Assert.Null(repository);
        Assert.Equal(0, number);
    }

    /// <summary>Open, merged or closed: the picker takes it by number, reads its state by
    /// number, and records the link under the task's repository.</summary>
    [Theory]
    [InlineData(GitHubItemState.Open)]
    [InlineData(GitHubItemState.Merged)]
    [InlineData(GitHubItemState.Closed)]
    public async Task A_pull_request_typed_by_number_is_linked_whatever_its_state(GitHubItemState state)
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.PullRequestsByNumber.Add(Linked(812, state));
        var reader = Reader(host);
        var section = RenderSection(host, row, reader);

        Type(section, "812");

        section.WaitForAssertion(() =>
        {
            Assert.Equal([new EntryPullRequestLink(Repository, 812)], row.PullRequestLinks);
            Assert.Equal(state, Assert.Single(reader.LinkedPullRequests).State);
            Assert.Contains($"{Repository} 812", host.Client.ReadByNumber);
            Assert.Empty(section.FindAll("[data-testid='work-link-picker-reference']"));
            Assert.Equal("JSdotNet/Backlog#812", section.Find("[data-testid='work-pull-request']").GetAttribute("data-pull-request"));
            Assert.Contains(host.Toasts.Visible, toast => toast.TestId == WorkLinker.LinkedTestId);
        });
    }

    /// <summary>A merged one is read once: the reader keeps it, so asking for it again
    /// does not ask GitHub again, and a read of the open list whose links were taken
    /// before the link was made does not drop it.</summary>
    [Fact]
    public async Task A_merged_pull_request_linked_by_its_link_is_kept_by_the_reader()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.PullRequestsByNumber.Add(Linked(655, GitHubItemState.Merged, mergedAt: Noon.AddDays(-3)));
        var reader = Reader(host);

        Assert.Null(await WorkLinker.LinkPullRequestAsync(host.State, host.Toasts, reader, row, "https://github.com/jsdotnet/backlog/pull/655/files"));

        Assert.Equal([new EntryPullRequestLink(Repository, 655)], row.PullRequestLinks);
        var (merged, failure) = await reader.ReadPullRequestAsync(new GitHubPullRequestReference(Repository, 655));
        Assert.Null(failure);
        Assert.Equal(Noon.AddDays(-3), merged!.MergedAt);
        Assert.Equal([$"{Repository} 655"], host.Client.ReadByNumber);

        // This reader is asked for no links, as one whose list was taken before the link.
        await reader.ReadAsync();
        await reader.LinkedRead;
        Assert.Equal(655, Assert.Single(reader.LinkedPullRequests).Number);
        Assert.Equal([$"{Repository} 655"], host.Client.ReadByNumber);
    }

    /// <summary>A task with two repositories does not say which one a bare number is
    /// in, so the link is asked for instead of guessing.</summary>
    [Fact]
    public async Task A_bare_number_on_a_task_with_two_repositories_asks_for_the_link()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository, "JSdotNet/Other");
        var row = await host.WriteEntryAsync("# Across two\n`prompt` `!in-progress` `repo:JSdotNet/Backlog` `repo:JSdotNet/Other`\n");
        var reader = Reader(host);

        var failure = await WorkLinker.LinkPullRequestAsync(host.State, host.Toasts, reader, row, "812");

        Assert.StartsWith("This task names more than one repository", failure, StringComparison.Ordinal);
        Assert.Empty(host.Client.ReadByNumber);
    }

    [Fact]
    public async Task A_pull_request_in_a_repository_not_registered_is_refused_under_the_field()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.PullRequestsByNumber.Add(Linked(5, GitHubItemState.Merged, repository: "someone/elsewhere"));
        var reader = Reader(host);
        var section = RenderSection(host, row, reader);

        Type(section, "https://github.com/someone/elsewhere/pull/5");

        section.WaitForAssertion(() =>
        {
            Assert.Contains("someone/elsewhere is not a repository registered in Settings", section.Find("[data-testid='work-link-picker-reference']").TextContent, StringComparison.Ordinal);
            Assert.Empty(row.PullRequestLinks);
            Assert.Empty(host.Client.ReadByNumber);

            // Said once, under the field it was typed into, not again as a toast.
            Assert.DoesNotContain(host.Toasts.Visible, toast => toast.TestId == WorkLinker.RefusedTestId);
        });
    }

    [Fact]
    public async Task A_number_GitHub_does_not_know_is_refused_and_nothing_is_recorded()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        var reader = Reader(host);

        var failure = await WorkLinker.LinkPullRequestAsync(host.State, host.Toasts, reader, row, "#99999");

        Assert.Equal("JSdotNet/Backlog has no pull request #99999.", failure);
        Assert.Empty(row.PullRequestLinks);
    }

    [Fact]
    public async Task Text_that_names_no_pull_request_is_refused_before_GitHub_is_asked()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        var reader = Reader(host);

        var failure = await WorkLinker.LinkPullRequestAsync(host.State, host.Toasts, reader, row, "the merged one");

        Assert.Equal("Type a pull request's number, or paste its link.", failure);
        Assert.Empty(host.Client.ReadByNumber);
    }

    /// <summary>A bare number is the task's own repository's; a task with none has to
    /// be given the link.</summary>
    [Fact]
    public async Task A_bare_number_on_a_task_with_no_registered_repository_asks_for_the_link()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync("# Loose notes\n`task` `!in-progress`\n");
        var reader = Reader(host);

        var failure = await WorkLinker.LinkPullRequestAsync(host.State, host.Toasts, reader, row, "812");

        Assert.StartsWith("This task names no repository registered in Settings", failure, StringComparison.Ordinal);
        Assert.Empty(row.PullRequestLinks);
    }

    /// <summary>A pull request offered as open that merged before it was chosen is
    /// still the work it was: it is linked, not refused as no longer open.</summary>
    [Fact]
    public async Task A_listed_pull_request_that_merged_before_it_was_chosen_is_still_linked()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        var reader = Reader(host);
        await reader.ReadAsync();

        var projection = new WorkLinksProjection(
            host.State.Rows,
            reader.Sessions,
            reader.PullRequests,
            host.State.RepositoryAliasFor,
            host.State.RepositoryColourFor,
            host.State.SessionOf,
            Noon,
            reader.SessionsRead,
            reader.LinkedPullRequests);
        var gone = new LooseWork(
            PullRequest: new Backlog.UI.Components.Integrations.WorkPullRequest(
                "JSdotNet/Backlog#700", 700, "Merged since", Backlog.UI.Components.Integrations.IntegrationArtifactState.Open),
            Repository: "Backlog");

        Assert.Null(await WorkLinker.LinkAsync(host.State, host.Toasts, projection, row, gone));
        Assert.Equal([new EntryPullRequestLink(Repository, 700)], row.PullRequestLinks);
    }

    private static void Type(IRenderedComponent<LinkedWorkSection> section, string reference)
    {
        section.Find("[data-testid='work-links-link']").Click();
        section.WaitForAssertion(() => Assert.NotEmpty(section.FindAll("[data-testid='work-link-picker-reference']")));

        section.Find("[data-testid='work-link-picker-reference-field'] input").Input(reference);
        section.Find("[data-testid='work-link-picker-reference']").Submit();
    }

    private static IRenderedComponent<LinkedWorkSection> RenderSection(TasksPaneHost host, EntryRow row, WorkInProgressReader reader) =>
        host.Context.Render<LinkedWorkSection>(parameters => parameters
            .Add(p => p.Row, row)
            .Add(p => p.Reader, reader));

    private static WorkInProgressReader Reader(TasksPaneHost host)
    {
        var clock = new FakeTimeProvider(Noon);
        host.Context.Services.AddSingleton<TimeProvider>(clock);

        return new(host.GitHub, new NoSessions(), clock);
    }

    private static GitHubLinkedPullRequest Linked(int number, GitHubItemState state, DateTimeOffset? mergedAt = null, string repository = Repository) =>
        new(number, $"https://github.com/{repository}/pull/{number}", $"Pull request {number}", repository, state, "someone-else",
            state is GitHubItemState.Merged ? mergedAt ?? Noon.AddHours(-1) : null);

    private sealed class NoSessions : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog([], [], 0));
    }
}
