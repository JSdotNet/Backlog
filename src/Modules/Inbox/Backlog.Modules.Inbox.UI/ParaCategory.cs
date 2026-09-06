namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// The PARA bucket an inbox item leans towards — the <c>PARA Category</c> of
/// <c>.domain/second-brain/domain.md</c>, restated here because the Inbox sits
/// upstream of Second Brain and may not reference it.
/// <para>
/// A lean and not a filing. The Inbox owns triage, and triage is where an item
/// is actually routed; this only says which drawer a reader would reach for
/// first, so the queue can be read one drawer at a time. Null on the item means
/// nobody has said, and the pane groups those as unsorted rather than guessing.
/// </para>
/// </summary>
public enum ParaCategory
{
    /// <summary>Active work with a goal and a deadline.</summary>
    Projects,

    /// <summary>Ongoing responsibilities without a deadline.</summary>
    Areas,

    /// <summary>Reference material and collected knowledge.</summary>
    Resources,

    /// <summary>Inactive items preserved for future search.</summary>
    Archive
}

/// <summary>The four buckets in the order PARA names them, and the word each
/// is read as.</summary>
public static class ParaCategories
{
    public static IReadOnlyList<ParaCategory> All { get; } =
        [ParaCategory.Projects, ParaCategory.Areas, ParaCategory.Resources, ParaCategory.Archive];

    public static string Label(ParaCategory category) => category switch
    {
        ParaCategory.Projects => "Projects",
        ParaCategory.Areas => "Areas",
        ParaCategory.Resources => "Resources",
        ParaCategory.Archive => "Archive",
        _ => category.ToString()
    };
}
