using Backlog.Desktop.UI.PullRequests;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Tests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Sessions context's pull request activity closing the notifications about a pull
/// request once a reading finds it merged or closed, and answering the startup probe's
/// question about it through Repository Management's contract (local ADR 0042,
/// <c>.devbook/domain/notifications/domain.md#subject</c>).
/// </summary>
public sealed class PullRequestSubjectsTests
{
    private readonly RecordingNotifications _notifications = new RecordingNotifications();

    [Theory]
    [InlineData(PullRequestState.Merged)]
    [InlineData(PullRequestState.Closed)]
    public async Task A_reading_that_finds_it_merged_or_closed_closes_the_notifications_about_it_once(PullRequestState state)
    {
        var subjects = new PullRequestSubjects(() => null, () => _notifications);
        var pull = Pull(42, "JSdotNet/Backlog") with { State = state };

        await subjects.ObserveAsync(pull, TestContext.Current.CancellationToken);
        await subjects.ObserveAsync(pull, TestContext.Current.CancellationToken);

        var ended = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("sessions", "pull-request", "jsdotnet/backlog#42"), ended.Subject);
        Assert.Equal("Pull request merged", ended.Reason);
    }

    [Fact]
    public async Task An_open_pull_request_closes_nothing()
    {
        var subjects = new PullRequestSubjects(() => null, () => _notifications);

        await subjects.ObserveAsync(Pull(42, "JSdotNet/Backlog"), TestContext.Current.CancellationToken);
        await subjects.ObserveAsync(Pull(43, "JSdotNet/Backlog") with { State = PullRequestState.Draft }, TestContext.Current.CancellationToken);

        Assert.Empty(_notifications.Closed);
    }

    [Fact]
    public async Task A_close_that_failed_is_tried_again_on_the_next_reading()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        var subjects = new PullRequestSubjects(() => null, () => _notifications);
        var merged = Pull(42, "JSdotNet/Backlog") with { State = PullRequestState.Merged };

        await subjects.ObserveAsync(merged, TestContext.Current.CancellationToken);
        _notifications.Fails = null;
        await subjects.ObserveAsync(merged, TestContext.Current.CancellationToken);

        Assert.Single(_notifications.Closed);
    }

    [Fact]
    public async Task The_session_card_reading_reports_what_it_found()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(1480, "JSdotNet/Backlog") with { State = PullRequestState.Merged });
        var subjects = new PullRequestSubjects(() => contract, () => _notifications);

        var status = await SessionPullRequestStatuses.ReadAsync(
            contract,
            new DeliveryRunReference(DeliveryRunReferenceKind.PullRequest, "PR #1480", Title: null, "https://github.com/JSdotNet/Backlog/pull/1480", "JSdotNet/Backlog"),
            subjects);

        Assert.NotNull(status);
        Assert.Equal("jsdotnet/backlog#1480", Assert.Single(_notifications.Closed).Subject.Id);
    }

    [Fact]
    public async Task The_probes_question_is_answered_by_a_read_through_the_contract_and_no_answer_is_null()
    {
        var contract = new InMemoryPullRequests().Holds(
            Pull(1, "JSdotNet/Backlog"),
            Pull(2, "JSdotNet/Backlog") with { State = PullRequestState.Merged },
            Pull(3, "JSdotNet/Backlog") with { State = PullRequestState.Closed });
        var subjects = new PullRequestSubjects(() => contract, () => _notifications);
        var withoutTheContract = new PullRequestSubjects(() => null, () => _notifications);

        Assert.True(await subjects.IsOpenAsync("jsdotnet/backlog", 1, TestContext.Current.CancellationToken));
        Assert.False(await subjects.IsOpenAsync("jsdotnet/backlog", 2, TestContext.Current.CancellationToken));
        Assert.False(await subjects.IsOpenAsync("jsdotnet/backlog", 3, TestContext.Current.CancellationToken));
        Assert.Null(await subjects.IsOpenAsync("jsdotnet/backlog", 404, TestContext.Current.CancellationToken));
        Assert.Null(await withoutTheContract.IsOpenAsync("jsdotnet/backlog", 1, TestContext.Current.CancellationToken));
        Assert.All(contract.Calls, call => Assert.StartsWith("read ", call, StringComparison.Ordinal));
    }

    private static PullRequestStatus Pull(int number, string repository) =>
        new(
            RepositoryFullName: repository,
            Number: number,
            Url: $"https://github.com/{repository}/pull/{number}",
            Title: "Close notifications when their subject ends",
            State: PullRequestState.Open,
            HeadRefName: "feature/subjects",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: true,
            UpdatedAt: DateTimeOffset.Parse("2026-10-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture))
        {
            Checks = PullRequestCheckState.Passing,
            MergeReady = true
        };
}
