namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Whether a merged pull request finishes the entry it was recorded on.
/// <para>
/// An entry is done when its pull request merges into the repository's default
/// branch or a <c>plan/&lt;slug&gt;</c> branch — not when the pull request opens,
/// and not when it merges into a stacked parent's branch, which has not landed
/// anywhere yet. Only a <c>prompt</c> entry: a <c>task</c> or <c>test</c> is the
/// person's own to tick.
/// </para>
/// <para>
/// Beside <see cref="EntryStatusFlow"/> and asking it, so the move is only ever one
/// the lifecycle graph allows: Ready has no edge to Done, and an entry that never
/// went In progress stays where it is rather than being walked past the graph.
/// Strings rather than GitHub types, so the MCP side and a future worker can ask
/// the same rule without referencing the GitHub client.
/// </para>
/// </summary>
public static class PullRequestLanding
{
    /// <summary>The prefix a plan branch carries.</summary>
    public const string PlanBranchPrefix = "plan/";

    /// <summary>
    /// True when an entry of <paramref name="type"/> at <paramref name="status"/>
    /// moves to Done because its pull request merged into <paramref name="baseRef"/>,
    /// in a repository whose default branch is <paramref name="defaultBranch"/>.
    /// Branch names compare ordinally, because Git's are case-sensitive; an unknown
    /// base or default branch finishes nothing.
    /// </summary>
    public static bool FinishesEntry(EntryType type, EntryStatus status, string? baseRef, string? defaultBranch)
    {
        if (type != EntryType.Prompt) return false;
        if (status == EntryStatus.Done || !EntryStatusFlow.IsAllowed(status, EntryStatus.Done)) return false;
        if (string.IsNullOrWhiteSpace(baseRef) || string.IsNullOrWhiteSpace(defaultBranch)) return false;

        return string.Equals(baseRef, defaultBranch, StringComparison.Ordinal)
            || baseRef.StartsWith(PlanBranchPrefix, StringComparison.Ordinal);
    }
}
