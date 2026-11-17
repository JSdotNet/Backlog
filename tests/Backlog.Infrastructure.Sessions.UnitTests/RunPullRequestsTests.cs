using System.Text.Json.Nodes;
using Backlog.Infrastructure.Sessions;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The pull requests a run recorded, read the same way by the gate before it asks its
/// Personal Validation question and by the Sessions probe that closes it afterwards, and
/// whether they all ended
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-notification-closes-when-its-subject-ends</c>).
/// </summary>
public sealed class RunPullRequestsTests : IDisposable
{
    private const string Worktree = @"D:\Repos\Backlog\.claude\worktrees\run-pull-requests-4a5b6c";
    private static readonly string[] Stages = ["Implement", "Personal Validation", "Create Pull Request"];

    private readonly string _home = Path.Combine(Path.GetTempPath(), "backlog-run-pull-requests-tests", Guid.NewGuid().ToString("n"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_file_and_its_reading_name_the_same_pull_requests_once_each_in_stage_order()
    {
        var surface = Surface();
        var run = (await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: Cancellation)).RunId;
        await surface.UpdateStageAsync(
            Worktree, run, 0, "done",
            links:
            [
                new("App", "http://localhost:5000", null),
                new("Draft", "https://github.com/JSdotNet/Backlog/pull/1700", null)
            ],
            cancellationToken: Cancellation);
        await surface.UpdateStageAsync(
            Worktree, run, 1, "in_progress",
            links:
            [
                new("Same, other spelling", "https://github.com/jsdotnet/backlog/pull/1700/files", null),
                new(null, "https://github.com/JSdotNet/Backlog-Desktop/pull/12", null)
            ],
            cancellationToken: Cancellation);

        var fromFile = RunPullRequests.Of((await new DeliveryRunStore(_home).ReadAsync(Worktree, run, Cancellation))!);
        var fromReading = RunPullRequests.Of((await surface.GetRunAsync(Worktree, run, Cancellation))!);

        Assert.Equal([("JSdotNet/Backlog", 1700, "Draft"), ("JSdotNet/Backlog-Desktop", 12, (string?)null)], fromFile.Select(pull => (pull.Repository, pull.Number, pull.Link.Label)));
        Assert.Equal(fromFile, fromReading);
    }

    [Fact]
    public async Task A_pull_request_named_only_by_the_runs_issue_field_or_summary_is_not_the_runs_own()
    {
        var surface = Surface();
        var run = (await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: Cancellation)).RunId;
        var store = new DeliveryRunStore(_home);
        var file = (await store.ReadAsync(Worktree, run, Cancellation))!;
        file["githubIssue"] = new JsonObject { ["url"] = "https://github.com/JSdotNet/Backlog/pull/9", ["number"] = 9 };
        file["summary"] = "Follows up https://github.com/JSdotNet/Backlog/pull/10.";
        await store.WriteAsync(Worktree, run, file, Cancellation);

        var reading = (await surface.GetRunAsync(Worktree, run, Cancellation))!;

        Assert.Empty(RunPullRequests.Of(file));
        Assert.Empty(RunPullRequests.Of(reading));
        Assert.Equal(2, reading.References.Count(reference => reference.Kind is DeliveryRunReferenceKind.PullRequest));
    }

    [Theory]
    [InlineData(new[] { SessionPullRequestState.Merged, SessionPullRequestState.Closed }, "Pull request merged")]
    [InlineData(new[] { SessionPullRequestState.Closed, SessionPullRequestState.Closed }, "Pull request closed")]
    [InlineData(new[] { SessionPullRequestState.Merged, SessionPullRequestState.Draft }, null)]
    [InlineData(new[] { SessionPullRequestState.Open, SessionPullRequestState.Merged }, null)]
    public async Task The_pull_requests_ended_only_once_every_one_merged_or_closed(SessionPullRequestState[] states, string? reason)
    {
        var answers = new Queue<SessionPullRequestState?>(states.Select(state => (SessionPullRequestState?)state));
        var port = new Answering(_ => Task.FromResult(answers.Dequeue()));

        Assert.Equal(reason, await RunPullRequests.EndAsync(Two(), port, Cancellation));
    }

    [Fact]
    public async Task No_pull_request_or_one_that_reads_as_nothing_has_not_ended()
    {
        Assert.Null(await RunPullRequests.EndAsync([], new Answering(_ => throw new InvalidOperationException("not asked")), Cancellation));
        Assert.Null(await RunPullRequests.EndAsync(Two(), new Answering(_ => Task.FromResult<SessionPullRequestState?>(null)), Cancellation));
    }

    [Fact]
    public async Task A_read_that_ignores_the_token_is_abandoned_once_the_token_cancels()
    {
        using var budget = new CancellationTokenSource();
        var never = new TaskCompletionSource<SessionPullRequestState?>();

        var ending = RunPullRequests.EndAsync(Two(), new Answering(_ => never.Task), budget.Token);
        await budget.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ending.WaitAsync(TimeSpan.FromSeconds(10), Cancellation));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private LocalDeliverySurfaceLifecycle Surface() => new(_home, "machine", "Workshop PC");

    private static RecordedPullRequest[] Two() =>
    [
        new(new DeliveryStageLink("PR", "https://github.com/JSdotNet/Backlog/pull/1", null), "JSdotNet/Backlog", 1),
        new(new DeliveryStageLink("PR", "https://github.com/JSdotNet/Backlog/pull/2", null), "JSdotNet/Backlog", 2)
    ];

    /// <summary>GitHub at its boundary, answering each read as the test says.</summary>
    private sealed class Answering(Func<int, Task<SessionPullRequestState?>> answer) : ISessionPullRequestStates
    {
        public Task<SessionPullRequestState?> StateAsync(string repository, int number, CancellationToken cancellationToken = default) =>
            answer(number);
    }
}
