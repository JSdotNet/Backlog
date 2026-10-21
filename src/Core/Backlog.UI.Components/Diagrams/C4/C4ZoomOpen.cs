namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// One request to open a view in the C4 zoom tab, made by a host — an arc42 chapter
/// offering "Open in C4 zoom" on the view it embeds.
/// <para>
/// A class matched by reference, not a key: asking twice for the same view, after the
/// reader zoomed elsewhere in between, has to open it twice. A new instance is a new
/// request; <see cref="C4ZoomExplorer"/> honours each one once.
/// </para>
/// </summary>
/// <param name="ViewKey">The view to open, by its key in the workspace.</param>
public sealed class C4ZoomOpen(string ViewKey)
{
    public string ViewKey { get; } = ViewKey;
}
