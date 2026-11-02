using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Modules.RepositoryManagement.UnitTests;

/// <summary>
/// The one merge a pull request is offered, wherever it is offered: a withdrawal while
/// GitHub holds an auto-merge, then nothing for a draft, a conflict or a stacked pull
/// request, then merge now where GitHub would merge it now, else merge when ready
/// (local ADR 0042; the pane's rule, which a task's row now follows too).
/// </summary>
public sealed class PullRequestMergesTests
{
    private static PullRequestStatus Pull(PullRequestState state = PullRequestState.Open) =>
        new("acme/app", 42, "https://github.com/acme/app/pull/42", "Add the thing", state,
            "feature/thing", "main", "octocat", ViewerDidAuthor: true, UpdatedAt: null);

    private static readonly PullRequestReference Parent = new("acme/app", 41);

    private static readonly Dictionary<string, (PullRequestStatus Pull, PullRequestMergeChoice? Expected)> Cases = new(StringComparer.Ordinal)
    {
        ["mergeable now"] = (Pull() with { MergeReady = true }, PullRequestMergeChoice.Now),
        ["not yet mergeable"] = (Pull(), PullRequestMergeChoice.WhenReady),
        ["held for auto-merge"] = (Pull() with { AutoMergeEnabled = true, MergeReady = true }, PullRequestMergeChoice.CancelAutoMerge),
        ["held and stacked"] = (Pull() with { AutoMergeEnabled = true, StackParent = Parent }, PullRequestMergeChoice.CancelAutoMerge),
        ["held and a draft"] = (Pull(PullRequestState.Draft) with { AutoMergeEnabled = true }, PullRequestMergeChoice.CancelAutoMerge),
        ["held and in conflict"] = (Pull() with { AutoMergeEnabled = true, HasConflicts = true }, PullRequestMergeChoice.CancelAutoMerge),
        ["a draft"] = (Pull(PullRequestState.Draft) with { MergeReady = true }, null),
        ["in conflict"] = (Pull() with { HasConflicts = true }, null),
        ["stacked"] = (Pull() with { MergeReady = true, StackParent = Parent }, null),
        ["merged"] = (Pull(PullRequestState.Merged) with { AutoMergeEnabled = true, MergeReady = true }, null),
        ["closed"] = (Pull(PullRequestState.Closed) with { MergeReady = true }, null)
    };

    public static TheoryData<string> Table() => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(Table))]
    public void The_merge_offered_follows_the_one_rule(string because)
    {
        var (pull, expected) = Cases[because];

        Assert.Equal(expected, PullRequestMerges.For(pull));
    }
}
