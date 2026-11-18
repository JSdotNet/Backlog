namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// The one stamping rule the Inbox applies wherever it writes a version the
/// replica will compare: now, but never at or before the stamp it has to
/// supersede. That stamp may come from another clock — the phone's, the
/// service's — and a desktop clock behind it would otherwise write a version the
/// replica refuses as stale (<c>TaskChangePrecedence</c>).
/// </summary>
internal static class InboxStamps
{
    /// <summary><paramref name="now"/> when it is later than
    /// <paramref name="stamp"/>, else one tick after <paramref name="stamp"/>.</summary>
    internal static DateTimeOffset After(DateTimeOffset now, DateTimeOffset stamp) =>
        now > stamp ? now : stamp.AddTicks(1);
}
