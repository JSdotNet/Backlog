namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// One thing waiting to be triaged, in the Inbox's own words.
/// <para>
/// This is the published contract the Inbox pane renders and the only shape a
/// supplying context has to produce — see <c>.domain/context-map.md</c>, where
/// Inbox sits upstream of Tasks and Second Brain. Keeping it a
/// plain record is what stops the pane from reaching into a backlog row for a
/// title.
/// </para>
/// <para>
/// The three positional members are the shape every supplier already produces;
/// what a reader needs to <em>sort</em> the queue by — what kind of thing it is,
/// where it came from, and the buckets it can be grouped under — rides as
/// init-only properties with honest defaults, so a supplier that knows none of
/// it still hands over a valid item and the pane still draws it.
/// </para>
/// </summary>
/// <param name="Key">Opaque identity handed back on open, so the supplying
/// context can find the item again without the Inbox knowing what it is.</param>
/// <param name="Title">What the item says it is, or empty when nobody has said
/// yet.</param>
/// <param name="Area">Where it is filed, or null for unfiled.</param>
public sealed record InboxItem(string Key, string Title, string? Area)
{
    /// <summary>What kind of content this is. <see cref="InboxItemKind.Text"/>
    /// when nobody has looked — a plain note is the one kind every capture
    /// source can produce.</summary>
    public InboxItemKind Kind { get; init; } = InboxItemKind.Text;

    /// <summary>Where the item came from, or null when the supplier does not
    /// record provenance.</summary>
    public InboxSource? Source { get; init; }

    /// <summary>The general tags on the item, stored bare (no <c>#</c>). Person
    /// tags are not here — a person is a <see cref="Source"/>, not a
    /// keyword.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The repository the item is about, as its supplier names it, or
    /// null when it is about none.</summary>
    public string? Repository { get; init; }

    /// <summary>The PARA bucket the item leans towards, or null when nobody has
    /// sorted it yet.</summary>
    public ParaCategory? Para { get; init; }
}
