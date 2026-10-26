using Backlog.Modules.Tasks.Abstractions;

using Xunit;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// When a pull request's merge finishes the entry it was recorded on: a
/// <c>prompt</c> entry, In progress, whose pull request merged into the
/// repository's default branch or a <c>plan/</c> branch.
/// </summary>
public class PullRequestLandingTests
{
    [Fact]
    public void A_prompt_in_progress_merged_into_the_default_branch_is_finished() =>
        Assert.True(PullRequestLanding.FinishesEntry(EntryType.Prompt, EntryStatus.InProgress, "main", "main"));

    [Fact]
    public void A_prompt_in_progress_merged_into_a_plan_branch_is_finished() =>
        Assert.True(PullRequestLanding.FinishesEntry(EntryType.Prompt, EntryStatus.InProgress, "plan/devbook-domain-view", "main"));

    /// <summary>A stacked pull request merges into its parent's branch, which is
    /// neither landed nor a plan.</summary>
    [Theory]
    [InlineData("claude/parent-branch")]
    [InlineData("Main")]
    [InlineData("planning")]
    [InlineData("feature/plan/x")]
    public void A_merge_into_any_other_branch_finishes_nothing(string baseRef) =>
        Assert.False(PullRequestLanding.FinishesEntry(EntryType.Prompt, EntryStatus.InProgress, baseRef, "main"));

    [Theory]
    [InlineData(null, "main")]
    [InlineData("", "main")]
    [InlineData("main", null)]
    [InlineData("main", " ")]
    [InlineData("plan/x", null)]
    [InlineData(null, null)]
    public void An_unknown_base_or_default_branch_finishes_nothing(string? baseRef, string? defaultBranch) =>
        Assert.False(PullRequestLanding.FinishesEntry(EntryType.Prompt, EntryStatus.InProgress, baseRef, defaultBranch));

    [Theory]
    [InlineData(EntryType.Task)]
    [InlineData(EntryType.Test)]
    [InlineData(EntryType.Idea)]
    public void Only_a_prompt_entry_is_finished_by_its_merge(EntryType type) =>
        Assert.False(PullRequestLanding.FinishesEntry(type, EntryStatus.InProgress, "main", "main"));

    /// <summary>Ready has no edge to Done in the lifecycle graph, so a Ready
    /// entry stays where it is rather than being walked past the graph.</summary>
    [Theory]
    [InlineData(EntryStatus.Ready)]
    [InlineData(EntryStatus.Done)]
    [InlineData(EntryStatus.Archived)]
    [InlineData(EntryStatus.Draft)]
    public void Only_an_entry_the_lifecycle_lets_reach_done_is_finished(EntryStatus status) =>
        Assert.False(PullRequestLanding.FinishesEntry(EntryType.Prompt, status, "main", "main"));
}
