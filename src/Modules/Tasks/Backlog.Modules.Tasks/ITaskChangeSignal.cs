namespace Backlog.Modules.Tasks;

/// <summary>
/// Says that a task was written on this machine, to whatever wants to act soon
/// after rather than on its own schedule.
/// <para>
/// It exists for cloud sync. The replication loop runs every five minutes, and
/// five minutes is the whole of the window in which two machines can edit the
/// same task without either knowing: a task retitled here and given a new status
/// there inside that window keeps one machine's whole document and drops the
/// other's, because the replica merges documents and not fields. Nothing about
/// the merge changes with this signal — what changes is how long the window stays
/// open. A loop that hears every local write can push it within seconds, and a
/// window of seconds is one a person has to try to hit.
/// </para>
/// <para>
/// It is a signal and not an event stream: it carries no task, no id and no
/// count, because the one listener wants to know that <em>something</em> changed
/// and will read what through the repository as it always has. A payload here
/// would be a second description of the change for the two to disagree about.
/// </para>
/// <para>
/// <see cref="Suppress"/> is for the one writer that is not a local change. The
/// pull applies the other machines' documents through the same repository every
/// edit goes through, and a write that arrived from the replica is not something
/// to tell the replica about — a signal raised there would start a cycle on the
/// heels of every cycle that received anything. The merge wraps its writes in a
/// suppression and the signal stays quiet for them alone; the scope is
/// asynchronous-local, so a person saving in the UI while a pull is applying is
/// still heard.
/// </para>
/// </summary>
public interface ITaskChangeSignal
{
    /// <summary>Raised after a task was written locally, unless the write ran
    /// inside a <see cref="Suppress"/> scope. On whatever thread wrote it.</summary>
    event Action? Changed;

    /// <summary>Tells the listeners a task was written. A no-op inside a
    /// <see cref="Suppress"/> scope.</summary>
    void Raise();

    /// <summary>Silences <see cref="Raise"/> for the rest of the current
    /// asynchronous flow, until the returned scope is disposed. Nests.</summary>
    IDisposable Suppress();
}
