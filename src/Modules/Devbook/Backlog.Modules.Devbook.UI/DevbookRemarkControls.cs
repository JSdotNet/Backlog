using Backlog.UI.Components.Markdown;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The remark controls a chapter panel hands its read view, and the remarks it
/// shows, for a repository that may be archived.
/// <para>
/// An archived repository's devbook is read-only (local ADR 0029 §4): remarks
/// already kept stay readable, and nothing more. Adding, resolving, editing and
/// deleting each write to the replicated annotation store, so all four are
/// withdrawn — the read view draws no control for a callback with no delegate.
/// The refusal is asked of the archive and not of the folder's editability: a
/// remark is the app's rather than the file's, so a branch snapshot of an
/// active repository keeps taking them.
/// </para>
/// <para>
/// One place for both panels that show remarks, the Architecture and Domain
/// panels, so the two cannot drift on which controls an archive withdraws.
/// </para>
/// </summary>
internal static class DevbookRemarkControls
{
    /// <summary>The four callbacks, or none of them while archived.</summary>
    public static DevbookRemarkCallbacks For(
        object receiver,
        bool repositoryArchived,
        Action<int> add,
        Action<string> resolve,
        Action<MarkdownComment> edit,
        Action<string> delete) =>
        repositoryArchived
            ? default
            : new DevbookRemarkCallbacks(
                EventCallback.Factory.Create(receiver, add),
                EventCallback.Factory.Create(receiver, resolve),
                EventCallback.Factory.Create(receiver, edit),
                EventCallback.Factory.Create(receiver, delete));

    /// <summary>The remarks to show. While archived, a draft nobody typed into
    /// is left out: it was never said, and the read view would open it in an
    /// editor whose save goes nowhere.</summary>
    public static IReadOnlyList<MarkdownComment> Shown(IReadOnlyList<MarkdownComment> comments, bool repositoryArchived) =>
        repositoryArchived
            ? [.. comments.Where(comment => !string.IsNullOrWhiteSpace(comment.Body))]
            : comments;
}

/// <summary>The remark callbacks a read view is bound to; <c>default</c> binds
/// none.</summary>
internal readonly record struct DevbookRemarkCallbacks(
    EventCallback<int> Add,
    EventCallback<string> Resolve,
    EventCallback<MarkdownComment> Edit,
    EventCallback<string> Delete);
