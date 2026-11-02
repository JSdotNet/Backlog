using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.Modules.RepositoryManagement.UnitTests.Fakes;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Time.Testing;
using static Backlog.Modules.RepositoryManagement.UnitTests.Fakes.Statuses;

namespace Backlog.Modules.RepositoryManagement.UnitTests;

/// <summary>
/// The commands of <see cref="IPullRequests"/> (local ADR 0042 §3): each acts on the
/// status kept for the pull request, reads its repository again once — whether GitHub
/// took the act or refused it — and says what changed, so every reader then sees the
/// new state with no call of its own (V10).
/// </summary>
public sealed class PullRequestCommandTests
{
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly CountingPullRequestSource _github = new();

    private IPullRequests Module() =>
        new PullRequests(_github, new FakeRegisteredRepositories(BacklogRepository, ArchifyRepository), new InMemorySettledStore(), _clock);

    public static TheoryData<string, string> Commands => new()
    {
        { "merge", "merge" },
        { "auto-merge on", "auto-merge" },
        { "auto-merge off", "cancel auto-merge" },
        { "update branch", "update" },
        { "mark ready", "ready" },
        { "re-run failed", "rerun" }
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Each_command_acts_once_reads_its_repository_again_once_and_says_what_changed(string command, string act)
    {
        _github.Holds(Pull(4), Pull(6, repository: ArchifyRepository));
        var module = Module();
        await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);
        var before = _github.Calls.Count;
        var changes = new List<PullRequestsChanged>();
        module.Changed += changes.Add;

        var result = await Run(module, command, Ref(4));

        Assert.True(result.IsSuccess);
        var calls = _github.Calls.Skip(before).ToList();
        Assert.Equal($"{act} {BacklogRepository}#4", calls[0]);
        Assert.Equal(1, calls.Count(call => call == $"list {BacklogRepository}"));
        Assert.DoesNotContain(calls, call => call.Contains(ArchifyRepository, StringComparison.Ordinal));
        var changed = Assert.Single(changes);
        Assert.Equal([BacklogRepository], changed.Repositories);
        Assert.Equal([Ref(4)], changed.PullRequests);
    }

    /// <summary>The pane lists the repository and reads its pins, and Progress reads the
    /// pull request by number, after a merge from a task's row: GitHub is asked nothing
    /// beyond the command's own re-read, and every one of them reads it merged.</summary>
    [Fact]
    public async Task After_a_merge_the_pane_and_progress_read_it_merged_with_no_further_call()
    {
        _github.Holds(Pull(4), Pull(5));
        var module = Module();
        await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);
        await module.ReadAsync([Ref(4)], cancellationToken: TestContext.Current.CancellationToken);

        var merged = await module.MergeAsync(Ref(4), TestContext.Current.CancellationToken);
        var afterAct = _github.Calls.Count;

        var paneList = await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        var panePins = await module.ReadAsync([Ref(4), Ref(5)], cancellationToken: TestContext.Current.CancellationToken);
        var progress = await module.ReadAsync([Ref(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(merged.IsSuccess);
        Assert.Equal([5], paneList.PullRequests.Select(pull => pull.Number));
        Assert.Equal(PullRequestState.Merged, panePins.PullRequests.Single(pull => pull.Number == 4).State);
        Assert.Equal(PullRequestState.Merged, Assert.Single(progress.PullRequests).State);
        Assert.Equal(afterAct, _github.Calls.Count);
        Assert.Equal([$"merge {BacklogRepository}#4", $"list {BacklogRepository}", $"read {BacklogRepository} 4"], _github.Calls.Skip(2).Take(3));
    }

    [Fact]
    public async Task A_refused_act_still_reads_the_repository_again_and_says_why()
    {
        _github.Holds(Pull(4)).RefusesActs("Pull request #4 is not mergeable: the base branch requires a review.");
        var module = Module();
        var raised = 0;
        module.Changed += _ => raised++;

        var result = await module.MergeAsync(Ref(4), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(PullRequestErrors.RefusedCode, result.Error.Code);
        Assert.Equal("Pull request #4 is not mergeable: the base branch requires a review.", result.Error.Message);
        Assert.Contains($"list {BacklogRepository}", _github.Calls);
        Assert.Equal(1, raised);
    }

    /// <summary>A command on a pull request nothing has read yet reads it first, so the
    /// act has the status — its node id, its head — to name it by.</summary>
    [Fact]
    public async Task A_command_on_a_pull_request_nothing_read_reads_it_first()
    {
        _github.Holds(Pull(8));
        var module = Module();

        var result = await module.UpdateBranchAsync(Ref(8), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([$"read {BacklogRepository} 8", $"update {BacklogRepository}#8", $"list {BacklogRepository}"], _github.Calls);
    }

    [Fact]
    public async Task A_command_on_a_pull_request_github_has_not_got_is_not_found_with_no_act()
    {
        var module = Module();

        var result = await module.MergeAsync(Ref(99), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(PullRequestErrors.NotFoundCode, result.Error.Code);
        Assert.Equal([$"read {BacklogRepository} 99"], _github.Calls);
    }

    [Fact]
    public async Task Auto_merge_switched_on_is_what_the_next_reader_sees()
    {
        _github.Holds(Pull(4));
        var module = Module();
        await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);

        await module.SetAutoMergeAsync(Ref(4), enable: true, TestContext.Current.CancellationToken);
        var read = await module.ReadAsync([Ref(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(read.PullRequests).AutoMergeEnabled);
    }

    /// <summary>A read of the repository still out when a command acts answers with the
    /// world before the act. A reader asking after the act is not handed it, and when it
    /// lands it is not kept over what the command's re-read found.</summary>
    [Fact]
    public async Task A_read_still_out_when_a_command_acts_is_neither_waited_on_nor_kept()
    {
        _github.Holds(Pull(4));
        var module = Module();
        await module.ReadAsync([Ref(4)], cancellationToken: TestContext.Current.CancellationToken);

        var held = new TaskCompletionSource();
        _github.Hold = held;
        var stale = module.ListMergedAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        var staleOpen = module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        _github.Hold = null;

        await module.MergeAsync(Ref(4), TestContext.Current.CancellationToken);
        _github.HoldsMerged(new MergedPullRequest(BacklogRepository, 4, "u", "t", "h", "main", "octocat", true, Noon, "octocat"));
        var fresh = await module.ListMergedAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        held.SetResult();
        await stale;
        await staleOpen;
        var later = await module.ListMergedAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        var open = await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, Assert.Single(fresh.PullRequests).Number);
        Assert.Equal(4, Assert.Single(later.PullRequests).Number);
        Assert.Empty(open.PullRequests);
    }

    /// <summary>
    /// Two commands in one repository whose re-reads overlap: the second merge is sent
    /// while the first's re-read is still out — held at its list, or at its read of the
    /// merged pull request by number. The second's re-read takes over the repository,
    /// but the first merge still lands: every reader then sees both merged, with no
    /// call of its own, and both are remembered across a restart.
    /// </summary>
    [Theory]
    [InlineData("list")]
    [InlineData("read")]
    public async Task Two_commands_overlapping_in_one_repository_both_land(string heldAt)
    {
        _github.Holds(Pull(1), Pull(2), Pull(3));
        var settled = new InMemorySettledStore();
        var module = new PullRequests(_github, new FakeRegisteredRepositories(BacklogRepository), settled, _clock);
        await module.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);

        var held = _github.HoldsNext(heldAt == "list" ? $"list {BacklogRepository}" : $"read {BacklogRepository} 1");
        var first = module.MergeAsync(Ref(1), TestContext.Current.CancellationToken);
        var second = await module.MergeAsync(Ref(2), TestContext.Current.CancellationToken);
        held.SetResult();
        var firstResult = await first;
        var calls = _github.Calls.Count;

        var read = await module.ReadAsync([Ref(1), Ref(2)], cancellationToken: TestContext.Current.CancellationToken);
        var open = await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(firstResult.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal([PullRequestState.Merged, PullRequestState.Merged], read.PullRequests.Select(pull => pull.State));
        Assert.Equal([3], open.PullRequests.Select(pull => pull.Number));
        Assert.Equal(calls, _github.Calls.Count);
        Assert.Equal([1, 2], settled.ReadAll().Select(pull => pull.Number).Order());
    }

    /// <summary>
    /// A re-read overtaken by a second command, whose list GitHub refused, still reads
    /// its pull request by number; landing late, that answer says what was true before
    /// the second act, so it is not kept — but nor does it throw out the newer answer
    /// the second command's list kept for the same pull request.
    /// </summary>
    [Fact]
    public async Task An_overtaken_re_read_leaves_the_newer_answer_a_later_command_kept()
    {
        _github.Holds(Pull(1), Pull(2));
        var module = Module();
        await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);

        _github.Refuses(BacklogRepository, "GitHub is not answering.");
        var held = _github.HoldsNext($"list {BacklogRepository}");
        var first = module.UpdateBranchAsync(Ref(1), TestContext.Current.CancellationToken);
        _github.Answers(BacklogRepository);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await module.UpdateBranchAsync(Ref(2), TestContext.Current.CancellationToken);
        held.SetResult();
        await first;
        var calls = _github.Calls.Count;

        var kept = module.Kept([Ref(1)]);
        var read = await module.ReadAsync([Ref(1)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(kept.PullRequests).Number);
        Assert.Equal(1, Assert.Single(read.PullRequests).Number);
        Assert.Equal(calls, _github.Calls.Count);
    }

    /// <summary>A transport that gives up while the re-read after a merge is out — an
    /// HTTP timeout surfaces as a cancellation nobody asked for — does not lose the
    /// merge GitHub already made: the caller is told it went through, and every other
    /// reader hears of it. What was kept for it before the act no longer holds, so the
    /// next reader asks GitHub rather than being told it is still open.</summary>
    [Fact]
    public async Task A_re_read_the_transport_gave_up_on_still_returns_what_the_act_did()
    {
        _github.Holds(Pull(4));
        var module = Module();
        await module.ListOpenAsync([BacklogRepository], cancellationToken: TestContext.Current.CancellationToken);
        var heard = 0;
        module.Changed += _ => heard++;
        _github.ReadThrows = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        var result = await module.MergeAsync(Ref(4), TestContext.Current.CancellationToken);
        _github.ReadThrows = null;
        var before = _github.Calls.Count;
        var read = await module.ReadAsync([Ref(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, heard);
        Assert.Equal([$"read {BacklogRepository} 4"], _github.Calls.Skip(before));
        Assert.Equal(PullRequestState.Merged, Assert.Single(read.PullRequests).State);
    }

    /// <summary>A reader whose handler throws on <c>Changed</c> does not turn a merge
    /// that went through into a failure for its caller, nor keep the next reader from
    /// hearing of it.</summary>
    [Fact]
    public async Task A_subscriber_that_throws_on_changed_neither_fails_the_command_nor_silences_the_others()
    {
        _github.Holds(Pull(4));
        var module = Module();
        var heard = 0;
        module.Changed += _ => throw new InvalidOperationException("A reader that could not redraw.");
        module.Changed += _ => heard++;

        var result = await module.MergeAsync(Ref(4), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, heard);
    }

    /// <summary>A caller that leaves while the re-read after its act is out still lets
    /// every other reader hear that the act was made.</summary>
    [Fact]
    public async Task A_caller_leaving_during_the_re_read_still_says_what_changed()
    {
        _github.Holds(Pull(4));
        var module = Module();
        await module.ReadAsync([Ref(4)], cancellationToken: TestContext.Current.CancellationToken);
        var heard = 0;
        module.Changed += _ => heard++;
        var held = _github.HoldsNext($"list {BacklogRepository}");
        using var leaving = new CancellationTokenSource();

        var merging = module.MergeAsync(Ref(4), leaving.Token);
        await leaving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => merging);
        Assert.Equal(1, heard);
        Assert.Empty(module.Kept([Ref(4)]).PullRequests);
        held.SetResult();
    }

    [Fact]
    public async Task A_command_with_no_repository_registered_is_not_configured()
    {
        var module = new PullRequests(_github, new FakeRegisteredRepositories(), new InMemorySettledStore(), _clock);

        var result = await module.MergeAsync(Ref(4), TestContext.Current.CancellationToken);

        Assert.Equal(PullRequestErrors.NotConfiguredCode, result.Error.Code);
        Assert.Empty(_github.Calls);
    }

    private static Task<Result> Run(IPullRequests module, string command, PullRequestReference pull) => command switch
    {
        "merge" => module.MergeAsync(pull, TestContext.Current.CancellationToken),
        "auto-merge on" => module.SetAutoMergeAsync(pull, enable: true, TestContext.Current.CancellationToken),
        "auto-merge off" => module.SetAutoMergeAsync(pull, enable: false, TestContext.Current.CancellationToken),
        "update branch" => module.UpdateBranchAsync(pull, TestContext.Current.CancellationToken),
        "mark ready" => module.MarkReadyForReviewAsync(pull, TestContext.Current.CancellationToken),
        "re-run failed" => module.RerunFailedAsync(pull, TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };
}
