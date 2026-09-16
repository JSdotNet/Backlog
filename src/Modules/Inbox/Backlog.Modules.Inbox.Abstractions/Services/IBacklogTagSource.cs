namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The tags the backlog already uses, offered to the Inbox's tag picker beside
/// the tags the inbox's own items carry.
/// <para>
/// A port on the Inbox's own surface rather than a reference to Tasks, for the
/// reason <see cref="IInboxBacklogTarget"/> gives: the Inbox is upstream of
/// Tasks on the context map and may not see it, so an adapter under
/// <c>src/Infrastructure</c> that is allowed to see both answers this from the
/// backlog. The same shape Tasks' own <c>IRoadmapTagSource</c> takes for the
/// plan's tags, and for the same reason.
/// </para>
/// <para>
/// Bare words, the way an inbox tag is stored — <c>infra</c>, never
/// <c>#infra</c> — and only the general ones. A backlog entry also wears
/// people (<c>@bob</c>) and roadmap items (<c>+release-q4</c>), and neither is
/// a word the Inbox can write back on to an entry: routing writes a tag on the
/// metadata line only when it opens with a letter, so offering one here would
/// offer a pick that routing drops.
/// </para>
/// </summary>
public interface IBacklogTagSource
{
    /// <summary>The distinct general tags in use across the backlog, bare, in
    /// the order they first appear. Empty when nothing is tagged.</summary>
    Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default);
}
