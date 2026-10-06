using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// One stack: open pull requests in one repository where each targets the head branch
/// of the one below it, in the order they have to be merged — the bottom first.
/// </summary>
/// <param name="Bottom">The one that targets a branch no open pull request owns, and so
/// the only one whose merge lands where the stack is going.</param>
/// <param name="Members">Every member, bottom first, each before what it carries. Where
/// two target the same parent, the older pull request comes first.</param>
internal sealed record PullRequestStack(GitHubOpenPullRequest Bottom, IReadOnlyList<GitHubOpenPullRequest> Members)
{
    /// <summary>The latest update to any member, which is where the stack sits among
    /// the others: a stack is as recent as its most recent change.</summary>
    public DateTimeOffset? UpdatedAt => Members.Max(pull => pull.UpdatedAt);
}

/// <summary>
/// The stacks among a list of open pull requests, derived from nothing but the branch
/// names GitHub already reports: a pull request whose base branch is the head branch of
/// another open one in the same repository sits on top of it.
/// <para>
/// Derived from every author's pull requests rather than from the rows on screen, so
/// that Mine hiding somebody else's bottom does not make the reader's own pull request
/// on top of it look like the bottom.
/// </para>
/// </summary>
internal sealed class PullRequestStacks
{
    private readonly Dictionary<GitHubOpenPullRequest, GitHubOpenPullRequest> _parents;
    private readonly Dictionary<GitHubOpenPullRequest, (PullRequestStack Stack, int Depth)> _places;

    private PullRequestStacks(
        IReadOnlyList<PullRequestStack> stacks,
        Dictionary<GitHubOpenPullRequest, GitHubOpenPullRequest> parents,
        Dictionary<GitHubOpenPullRequest, (PullRequestStack, int)> places)
    {
        Stacks = stacks;
        _parents = parents;
        _places = places;
    }

    /// <summary>Every stack, the most recently updated first.</summary>
    public IReadOnlyList<PullRequestStack> Stacks { get; }

    /// <summary>The pull request this one waits on, or null for the bottom of a stack
    /// and for one in no stack.</summary>
    public GitHubOpenPullRequest? ParentOf(GitHubOpenPullRequest pull) => _parents.GetValueOrDefault(pull);

    /// <summary>The stack this pull request is in, or null for none.</summary>
    public PullRequestStack? StackOf(GitHubOpenPullRequest pull) =>
        _places.TryGetValue(pull, out var place) ? place.Stack : null;

    /// <summary>How far up its stack it sits, from 0 at the bottom — how deep the tree
    /// indents it — or null for one in no stack. Two on the same parent share it.</summary>
    public int? DepthOf(GitHubOpenPullRequest pull) =>
        _places.TryGetValue(pull, out var place) ? place.Depth : null;

    public static PullRequestStacks Of(IEnumerable<GitHubOpenPullRequest> pulls)
    {
        var all = pulls.Distinct().ToList();

        // Two open pull requests from one head branch are possible; the most recently
        // updated is the one the branch is taken to mean.
        var byHead = all
            .GroupBy(pull => (Repository: pull.RepositoryFullName.ToUpperInvariant(), pull.HeadRefName))
            .ToDictionary(group => group.Key, group => group.MaxBy(pull => pull.UpdatedAt)!);

        var parents = new Dictionary<GitHubOpenPullRequest, GitHubOpenPullRequest>();
        foreach (var pull in all)
        {
            if (byHead.TryGetValue((pull.RepositoryFullName.ToUpperInvariant(), pull.BaseRefName), out var parent) && parent != pull)
            {
                parents[pull] = parent;
            }
        }

        // Two pull requests can target each other's branches. Such a loop has no
        // bottom to merge first, so its members are left out of every stack rather
        // than one of them being picked as the bottom at random.
        foreach (var pull in all.Where(pull => InLoop(pull, parents)).ToList())
        {
            parents.Remove(pull);
        }

        var children = parents
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.OrderBy(child => child.Number).ToList());

        var places = new Dictionary<GitHubOpenPullRequest, (PullRequestStack, int)>();
        var stacks = new List<PullRequestStack>();

        foreach (var bottom in all.Where(pull => !parents.ContainsKey(pull) && children.ContainsKey(pull)))
        {
            var members = new List<(GitHubOpenPullRequest Pull, int Depth)>();
            Walk(bottom, 0);

            var stack = new PullRequestStack(bottom, [.. members.Select(member => member.Pull)]);
            stacks.Add(stack);
            foreach (var (pull, depth) in members) places[pull] = (stack, depth);

            void Walk(GitHubOpenPullRequest pull, int depth)
            {
                members.Add((pull, depth));
                foreach (var child in children.GetValueOrDefault(pull) ?? []) Walk(child, depth + 1);
            }
        }

        return new PullRequestStacks([.. stacks.OrderByDescending(stack => stack.UpdatedAt)], parents, places);
    }

    private static bool InLoop(GitHubOpenPullRequest pull, Dictionary<GitHubOpenPullRequest, GitHubOpenPullRequest> parents)
    {
        var seen = new HashSet<GitHubOpenPullRequest>();
        for (var current = pull; parents.TryGetValue(current, out var parent); current = parent)
        {
            if (parent == pull) return true;
            if (!seen.Add(parent)) return false;
        }

        return false;
    }
}
