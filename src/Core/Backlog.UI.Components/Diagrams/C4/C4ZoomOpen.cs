namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// One request to open a view in the C4 zoom tab, made by a host — an arc42 chapter
/// offering "Open in C4 zoom" on the view it embeds, or a decision record on the
/// element it lands on.
/// <para>
/// A class matched by reference, not a key: asking twice for the same view, after the
/// reader zoomed elsewhere in between, has to open it twice. A new instance is a new
/// request; <see cref="C4ZoomExplorer"/> honours each one once.
/// </para>
/// </summary>
/// <param name="ViewKey">The view to open, by its key in the workspace.</param>
/// <param name="SelectId">The element to leave selected on it, by its identifier in
/// the workspace, or null to select nothing.</param>
/// <param name="OpenCode">Whether to open the Code level of <paramref name="SelectId"/>
/// over the view — the architecture surface's "code" line. Ignored for an element
/// with no Code level.</param>
public sealed class C4ZoomOpen(string ViewKey, string? SelectId = null, bool OpenCode = false)
{
    public string ViewKey { get; } = ViewKey;

    public string? SelectId { get; } = SelectId;

    public bool OpenCode { get; } = OpenCode;
}
