using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Modules.RepositoryManagement.Abstractions;

/// <summary>
/// One stack: open pull requests in one repository where each targets the head branch
/// of the one below it, in the order they have to be merged — the bottom first.
/// </summary>
/// <param name="Bottom">The one that targets a branch no open pull request owns, and so
/// the only one whose merge lands where the stack is going.</param>
/// <param name="Members">Every member, bottom first, each before what it carries. Where
/// two target the same parent, the older pull request comes first.</param>
public sealed record PullRequestStack(PullRequestStatus Bottom, IReadOnlyList<PullRequestStatus> Members)
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
/// on top of it look like the bottom. A pull request is found by its
/// <see cref="PullRequestReference.Key"/>, without regard to case, so a newer read of
/// it, or the same one with its parent set, is still found.
/// </para>
/// </summary>
public sealed class PullRequestStacks
{
    private readonly Dictionary<string, PullRequestStatus> _parents;
    private readonly Dictionary<string, (PullRequestStack Stack, int Depth)> _places;

    private PullRequestStacks(
        IReadOnlyList<PullRequestStack> stacks,
        Dictionary<string, PullRequestStatus> parents,
        Dictionary<string, (PullRequestStack, int)> places)
    {
        Stacks = stacks;
        _parents = parents;
        _places = places;
    }

    /// <summary>Every stack, the most recently updated first.</summary>
    public IReadOnlyList<PullRequestStack> Stacks { get; }

    /// <summary>The pull request this one waits on, or null for the bottom of a stack
    /// and for one in no stack.</summary>
    public PullRequestStatus? ParentOf(PullRequestStatus pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return _parents.GetValueOrDefault(pull.Reference.Key);
    }

    /// <summary>The stack this pull request is in, or null for none.</summary>
    public PullRequestStack? StackOf(PullRequestStatus pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return _places.TryGetValue(pull.Reference.Key, out var place) ? place.Stack : null;
    }

    /// <summary>How far up its stack it sits, from 0 at the bottom — how deep the tree
    /// indents it — or null for one in no stack. Two on the same parent share it.</summary>
    public int? DepthOf(PullRequestStatus pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return _places.TryGetValue(pull.Reference.Key, out var place) ? place.Depth : null;
    }

    /// <summary>The stacks among <paramref name="pulls"/>. Pass open pull requests only:
    /// a merged or closed one has no branch left to stack on.</summary>
    public static PullRequestStacks Of(IEnumerable<PullRequestStatus> pulls)
    {
        ArgumentNullException.ThrowIfNull(pulls);

        // One pull request per key: a list and a pin can both carry it.
        var all = pulls
            .GroupBy(pull => pull.Reference.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        // Two open pull requests from one head branch are possible; the most recently
        // updated is the one the branch is taken to mean.
        var byHead = all
            .GroupBy(pull => (Repository: pull.RepositoryFullName.Trim().ToUpperInvariant(), pull.HeadRefName))
            .ToDictionary(group => group.Key, group => group.MaxBy(pull => pull.UpdatedAt)!);

        var parents = new Dictionary<string, PullRequestStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var pull in all)
        {
            if (byHead.TryGetValue((pull.RepositoryFullName.Trim().ToUpperInvariant(), pull.BaseRefName), out var parent)
                && !SameKey(parent, pull))
            {
                parents[pull.Reference.Key] = parent;
            }
        }

        return Build(all, parents);
    }

    /// <summary>
    /// The stacks among pull requests already placed (<see cref="PlaceAmong"/>): each sits
    /// on the one its <see cref="PullRequestStatus.StackParent"/> names, where that one is
    /// among <paramref name="placed"/>, whatever the branches say — so the tree a screen
    /// draws is the placing its verdicts read. Pass open pull requests only.
    /// </summary>
    public static PullRequestStacks OfPlaced(IEnumerable<PullRequestStatus> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        var all = placed
            .GroupBy(pull => pull.Reference.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var byKey = all.ToDictionary(pull => pull.Reference.Key, StringComparer.OrdinalIgnoreCase);

        var parents = new Dictionary<string, PullRequestStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var pull in all)
        {
            if (pull.StackParent is { } parent && byKey.TryGetValue(parent.Key, out var held) && !SameKey(held, pull))
            {
                parents[pull.Reference.Key] = held;
            }
        }

        return Build(all, parents);
    }

    /// <summary>The stacks the parent relation <paramref name="parents"/> makes among
    /// <paramref name="all"/>, bottom first, loops left out.</summary>
    private static PullRequestStacks Build(List<PullRequestStatus> all, Dictionary<string, PullRequestStatus> parents)
    {
        // Two pull requests can target each other's branches. Such a loop has no
        // bottom to merge first, so its members are left out of every stack rather
        // than one of them being picked as the bottom at random.
        foreach (var pull in all.Where(pull => InLoop(pull, parents)).ToList())
        {
            parents.Remove(pull.Reference.Key);
        }

        var byKey = all.ToDictionary(pull => pull.Reference.Key, StringComparer.OrdinalIgnoreCase);
        var children = parents
            .GroupBy(pair => pair.Value.Reference.Key, pair => byKey[pair.Key], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(child => child.Number).ToList(), StringComparer.OrdinalIgnoreCase);

        var places = new Dictionary<string, (PullRequestStack, int)>(StringComparer.OrdinalIgnoreCase);
        var stacks = new List<PullRequestStack>();

        foreach (var bottom in all.Where(pull => !parents.ContainsKey(pull.Reference.Key) && children.ContainsKey(pull.Reference.Key)))
        {
            var members = new List<(PullRequestStatus Pull, int Depth)>();
            Walk(bottom, 0);

            var stack = new PullRequestStack(bottom, [.. members.Select(member => member.Pull)]);
            stacks.Add(stack);
            foreach (var (pull, depth) in members) places[pull.Reference.Key] = (stack, depth);

            void Walk(PullRequestStatus pull, int depth)
            {
                members.Add((pull, depth));
                foreach (var child in children.GetValueOrDefault(pull.Reference.Key) ?? []) Walk(child, depth + 1);
            }
        }

        return new PullRequestStacks([.. stacks.OrderByDescending(stack => stack.UpdatedAt)], parents, places);
    }

    /// <summary>
    /// Each pull request with <see cref="PullRequestStatus.StackParent"/> set from the
    /// stacks among the open ones, in the order given — so a reader that hands the result
    /// to <see cref="PullRequestVerdict.Of(PullRequestStatus)"/> gets the verdict that
    /// names the parent. A merged or closed pull request is in no stack and waits on
    /// nothing.
    /// </summary>
    public static IReadOnlyList<PullRequestStatus> WithParents(IReadOnlyList<PullRequestStatus> pulls)
    {
        ArgumentNullException.ThrowIfNull(pulls);

        var stacks = Of(pulls.Where(pull => pull.IsOpen));

        return [.. pulls.Select(stacks.Place)];
    }

    /// <summary>
    /// The pull request with <see cref="PullRequestStatus.StackParent"/> set to the
    /// parent these stacks name for it — or cleared, where they name none or it is
    /// merged or closed. The same instance when that is what it already says.
    /// </summary>
    public PullRequestStatus Place(PullRequestStatus pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var parent = pull.IsOpen ? ParentOf(pull)?.Reference : null;
        return pull.StackParent == parent ? pull : pull with { StackParent = parent };
    }

    /// <summary>
    /// One pull request read by number, placed in its stack — the one rule every screen
    /// that shows a single pull request (the task row, the session card) places it by, so
    /// they and the Pull requests pane cannot give it different verdicts (local ADR 0042,
    /// V2).
    /// <list type="number">
    /// <item>Merged or closed: placed, with no parent — it waits on nothing.</item>
    /// <item>Held by its repository's <paramref name="openList"/> with a parent: that
    /// parent. One the list holds with none goes on to the rules below.</item>
    /// <item>Based on its repository's default branch: placed, with no parent, whatever
    /// the list — which is capped, may be refused, and is not kept for an unregistered or
    /// archived repository.</item>
    /// <item>Based on the head of an open pull request <paramref name="openList"/> holds:
    /// stacked on that one.</item>
    /// <item>Otherwise, with a list read: placed, with no parent — no open pull request
    /// the list holds is what it is based on, which is what the Pull requests pane's own
    /// stacks conclude.</item>
    /// <item>Otherwise, with no list: null — it might wait on a pull request nobody could
    /// list, so neither its place nor its verdict can be told.</item>
    /// </list>
    /// </summary>
    /// <param name="pull">The pull request, as read by number.</param>
    /// <param name="openList">Its repository's open list, placed
    /// (<see cref="WithParents"/>), or null where it could not be read.</param>
    public static PullRequestStatus? PlaceAmong(PullRequestStatus pull, IReadOnlyList<PullRequestStatus>? openList)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return Place(pull, openList ?? [], whole: openList is not null);
    }

    /// <summary>
    /// The rules of <see cref="PlaceAmong"/> over the rows a list holds, which place
    /// whatever they show — a listed pull request's own parent, a listed head — with
    /// <paramref name="whole"/> saying whether the list holds every open pull request of
    /// the repository, the one thing that lets absence mean "no parent".
    /// </summary>
    private static PullRequestStatus? Place(PullRequestStatus pull, IReadOnlyList<PullRequestStatus> rows, bool whole)
    {
        if (!pull.IsOpen) return pull.StackParent is null ? pull : pull with { StackParent = null };

        var sameRepository = rows
            .Where(row => row.IsOpen && string.Equals(row.RepositoryFullName.Trim(), pull.RepositoryFullName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A parent the list gives a row it holds is one it read. A listed row with none is
        // left to the rules below: "none" is read from absence, which only a whole list
        // can say.
        if (sameRepository.FirstOrDefault(row => SameKey(row, pull)) is { StackParent: { } listedParent })
        {
            return pull with { StackParent = listedParent };
        }

        if (!string.IsNullOrEmpty(pull.DefaultBranch) && string.Equals(pull.BaseRefName, pull.DefaultBranch, StringComparison.Ordinal))
        {
            return pull.StackParent is null ? pull : pull with { StackParent = null };
        }

        if (Of([.. sameRepository, pull]).ParentOf(pull) is { } parent)
        {
            return pull with { StackParent = parent.Reference };
        }

        // The whole repository was listed and no open pull request in it is what this one
        // is based on: it waits on none, as the pane's own stacks conclude. Short of the
        // whole repository it might wait on one the list does not hold, so its place
        // cannot be told.
        if (!whole) return null;
        return pull.StackParent is null ? pull : pull with { StackParent = null };
    }

    /// <summary>
    /// <see cref="PlaceAmong"/> over a listing as the contract answers it. What the listing
    /// holds places whatever it shows, capped or not: a listed pull request's own parent,
    /// and a listed head a pull request is based on. Only "no parent", which is read from
    /// absence, needs the listing to hold the repository whole
    /// (<see cref="PullRequestListing.CompleteRepositories"/>): one it failed, capped, or
    /// does not list at all may have an open parent beyond it, so a pull request nothing
    /// it holds explains — and not based on the default branch — cannot be placed. No
    /// listing at all holds nothing.
    /// </summary>
    public static PullRequestStatus? PlaceAmongListing(PullRequestStatus pull, PullRequestListing? openListing)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var whole = openListing is not null && openListing.CompleteRepositories.Any(repository =>
            string.Equals(repository.Trim(), pull.RepositoryFullName.Trim(), StringComparison.OrdinalIgnoreCase));

        return Place(pull, openListing?.PullRequests ?? [], whole);
    }

    private static bool SameKey(PullRequestStatus pull, PullRequestStatus other) =>
        string.Equals(pull.Reference.Key, other.Reference.Key, StringComparison.OrdinalIgnoreCase);

    private static bool InLoop(PullRequestStatus pull, Dictionary<string, PullRequestStatus> parents)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var current = pull; parents.TryGetValue(current.Reference.Key, out var parent); current = parent)
        {
            if (SameKey(parent, pull)) return true;
            if (!seen.Add(parent.Reference.Key)) return false;
        }

        return false;
    }
}
