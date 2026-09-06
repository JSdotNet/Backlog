namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// How the rows inside a PARA drawer are split: not at all, or under one of two
/// kinds of header.
/// <para>
/// PARA is the Inbox's structure and not one of these. The drawers — Projects,
/// Areas, Resources, Archive, and the Unsorted that PARA does not name — are
/// always there; a lens only says how the rows inside each drawer are
/// subdivided. <see cref="None"/> is not "flat": a drawer keeps its own natural
/// subdivision, which for Projects is one section per project and for Areas one
/// per area, because that is what those two drawers are made of.
/// </para>
/// </summary>
public enum InboxGrouping
{
    /// <summary>Each drawer's own subdivision: Projects per project, Areas per
    /// area, the rest as one list.</summary>
    None,

    /// <summary>One section per tag inside every drawer; an item with three
    /// tags sits in three sections, and an item with none under "Untagged".</summary>
    Tag,

    /// <summary>One section per repository inside every drawer, then the items
    /// about none.</summary>
    Repository
}

/// <summary>One section header and the items under it.</summary>
/// <param name="Key">A stable, slug-safe identity, unique within the pane, used
/// for the fold state and the region id.</param>
/// <param name="Name">What the header reads.</param>
/// <param name="Items">The items under it, in the supplier's order.</param>
public sealed record InboxGroup(string Key, string Name, IReadOnlyList<InboxItem> Items);

/// <summary>
/// One PARA drawer: its name, everything in it, and how it is sectioned.
/// </summary>
/// <param name="Key">A stable, slug-safe identity for the drawer.</param>
/// <param name="Name">What the drawer reads.</param>
/// <param name="Items">Every item in the drawer, each once, in the supplier's
/// order — the count the drawer's header shows.</param>
/// <param name="Sections">The subdivision, or empty when the drawer is one
/// list. An item may sit in more than one section under the tag lens, which is
/// why <see cref="Items"/> is kept separately.</param>
public sealed record InboxDrawer(string Key, string Name, IReadOnlyList<InboxItem> Items, IReadOnlyList<InboxGroup> Sections)
{
    /// <summary>Whether the drawer is drawn as sections rather than one list.</summary>
    public bool IsSectioned => Sections.Count > 0;
}

/// <summary>
/// The structure, computed rather than rendered, so the drawers, their sections
/// and their counts can be asserted without a DOM around them.
/// <para>
/// Drawers keep PARA's own order and Unsorted comes last; a drawer over nothing
/// is not emitted, because a header over nothing is a promise the list cannot
/// keep. Sections inside a drawer are alphabetical, because there is no other
/// order a reader could predict, and the fallback section — Untagged, No
/// repository, No area — is always last and only present when something is in
/// it.
/// </para>
/// </summary>
public static class InboxGroups
{
    public const string UnsortedKey = "unsorted";
    public const string UntaggedKey = "untagged";
    public const string NoRepositoryKey = "no-repository";
    public const string NoAreaKey = "no-area";

    /// <summary>The drawers, in order, for a lens.</summary>
    public static IReadOnlyList<InboxDrawer> Build(IReadOnlyList<InboxItem> items, InboxGrouping lens)
    {
        var drawers = new List<InboxDrawer>();

        foreach (var category in ParaCategories.All)
        {
            var members = items.Where(item => item.Para == category).ToList();
            if (members.Count == 0) continue;

            var key = Slug(category.ToString());
            drawers.Add(new InboxDrawer(key, ParaCategories.Label(category), members, Sectioned(key, category, members, lens)));
        }

        var unsorted = items.Where(item => item.Para is null).ToList();
        if (unsorted.Count > 0)
        {
            drawers.Add(new InboxDrawer(UnsortedKey, "Unsorted", unsorted, Sectioned(UnsortedKey, null, unsorted, lens)));
        }

        return drawers;
    }

    /// <summary>How one drawer is split under a lens. Under <see cref="InboxGrouping.None"/>
    /// a drawer keeps what it is made of: Projects are projects, so one section per
    /// repository; Areas are areas, so one per area; the other three are one list.</summary>
    private static IReadOnlyList<InboxGroup> Sectioned(string drawerKey, ParaCategory? category, IReadOnlyList<InboxItem> members, InboxGrouping lens) =>
        lens switch
        {
            InboxGrouping.Tag => ByTag(drawerKey, members),
            InboxGrouping.Repository => ByRepository(drawerKey, members),
            _ => category switch
            {
                ParaCategory.Projects => ByRepository(drawerKey, members),
                ParaCategory.Areas => ByArea(drawerKey, members),
                _ => []
            }
        };

    private static List<InboxGroup> ByTag(string drawerKey, IReadOnlyList<InboxItem> items)
    {
        var tagged = items
            .SelectMany(item => item.Tags
                .Select(tag => tag.Trim().TrimStart('#'))
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tag => (Tag: tag, Item: item)))
            .GroupBy(pair => pair.Tag, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new InboxGroup(
                $"{drawerKey}-tag-{Slug(group.Key)}",
                "#" + group.Key,
                group.Select(pair => pair.Item).ToList()))
            .ToList();

        var untagged = items
            .Where(item => !item.Tags.Any(tag => tag.Trim().TrimStart('#').Length > 0))
            .ToList();

        if (untagged.Count > 0)
        {
            tagged.Add(new InboxGroup($"{drawerKey}-{UntaggedKey}", "Untagged", untagged));
        }

        return tagged;
    }

    private static List<InboxGroup> ByRepository(string drawerKey, IReadOnlyList<InboxItem> items) =>
        ByField(drawerKey, items, item => item.Repository, "repo", $"{drawerKey}-{NoRepositoryKey}", "No repository");

    private static List<InboxGroup> ByArea(string drawerKey, IReadOnlyList<InboxItem> items) =>
        ByField(drawerKey, items, item => item.Area, "area", $"{drawerKey}-{NoAreaKey}", "No area");

    /// <summary>One section per distinct value of a single-valued field, then
    /// the items that have none.</summary>
    private static List<InboxGroup> ByField(
        string drawerKey,
        IReadOnlyList<InboxItem> items,
        Func<InboxItem, string?> field,
        string prefix,
        string fallbackKey,
        string fallbackName)
    {
        var named = items
            .Where(item => !string.IsNullOrWhiteSpace(field(item)))
            .GroupBy(item => field(item)!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new InboxGroup($"{drawerKey}-{prefix}-{Slug(group.Key)}", group.Key, group.ToList()))
            .ToList();

        var none = items.Where(item => string.IsNullOrWhiteSpace(field(item))).ToList();
        if (none.Count > 0)
        {
            named.Add(new InboxGroup(fallbackKey, fallbackName, none));
        }

        return named;
    }

    /// <summary>A key safe to put in an element id: letters, digits and hyphens,
    /// lower-cased. Two names that only differ in case or punctuation collapse to
    /// one key, which is the right answer for a fold state.</summary>
    public static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        var slug = new string(chars).Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "group" : slug;
    }
}
