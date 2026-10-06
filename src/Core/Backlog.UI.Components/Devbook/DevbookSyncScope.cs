using Backlog.SharedKernel.Devbook;

namespace Backlog.UI.Components.Devbook;

/// <summary>
/// What a devbook pane tells the records it draws about <c>sync</c>: where each
/// block sits, the direction in force there, and how to change it.
///
/// <para>Cascaded rather than passed down, because the record that draws a
/// block's headline sits four components below the pane that knows the file — the
/// file view, the document, the Markdown view, the chapter's record — and a
/// direction is resolved from blocks none of those hold: the page above the unit,
/// its <c>context.md</c>, the folder overview. The pane reads those; the record
/// asks it by the same address a status change reports, the block's level and its
/// heading text, and the pane maps that to an item path exactly as it does for a
/// status.</para>
///
/// <para>A pane that cascades none gets what it always had: a record stating
/// <c>sync</c> shows the word, read-only, and offers nothing.</para>
/// </summary>
/// <param name="describe">The state of the block at a level and heading, or null
/// for a block that is no sync level — it then shows only what it states.</param>
/// <param name="change">Writes a direction to that block, null removing the
/// field. Null for a pane that cannot write — a folder read from a branch.</param>
public sealed class DevbookSyncScope(
    Func<DevbookMetadataLevel, string?, DevbookSyncState?> describe,
    Func<DevbookMetadataLevel, string?, string?, Task>? change = null)
{
    /// <summary>Whether a reader may change a direction here.</summary>
    public bool CanChange => change is not null;

    /// <summary>The block's state, or null for one that takes no direction.</summary>
    public DevbookSyncState? Describe(DevbookMetadataLevel level, string? heading) => describe(level, heading);

    /// <summary>Write <paramref name="direction"/> to the block, or remove the
    /// field for null. A scope that cannot write does nothing.</summary>
    public Task ChangeAsync(DevbookMetadataLevel level, string? heading, string? direction) =>
        change is null ? Task.CompletedTask : change(level, heading, direction);
}
