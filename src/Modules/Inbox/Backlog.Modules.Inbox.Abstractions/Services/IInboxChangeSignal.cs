namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// Says that the Inbox was written on this machine through the MCP server an
/// assistant drives — a capture or an archive on a request thread — so an open
/// pane can reload rather than go on showing the queue it read when it opened.
/// The MCP tools are its only raisers today; another writer outside the pane
/// that should reach an open Inbox raises it too.
/// <para>
/// The Inbox's twin of Tasks' <c>ITaskChangeSignal</c>, kept smaller: a signal
/// and not an event stream, carrying no item, because a listener reads what
/// changed through <see cref="IInboxItems"/> as it always does. It has no
/// suppression scope, because nothing that writes on the replica's behalf raises
/// it — the shell already reloads the Inbox on the sync worker's own event —
/// and the pane's own writes do not raise it either: each one reloads the pane
/// already, and hearing it back would read the store twice.
/// </para>
/// </summary>
public interface IInboxChangeSignal
{
    /// <summary>Raised after the Inbox was written outside the pane, on whatever
    /// thread wrote it.</summary>
    event Action? Changed;

    /// <summary>Tells the listeners the Inbox was written.</summary>
    void Raise();
}
