namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Where an agent session a task links to stands now, in the words the Sessions
/// context uses for it (<c>.devbook/domain/sessions/domain.md#session-state</c>).
/// <para>
/// Tasks' own copy of three states rather than a reference to Sessions: a screen asks
/// its own module, and an infrastructure adapter that may see both contexts translates
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// There is deliberately no waiting-for-input member, because nothing a session leaves
/// on disk says whether a question was asked; <see cref="Stalled"/> is the closest
/// fact there is — quiet for longer than the Sessions threshold — and it is the state
/// that asks for the person.
/// </para>
/// </summary>
public enum LinkedSessionState
{
    /// <summary>On the machine now, and something moved recently.</summary>
    Running,

    /// <summary>Still live, but nothing has moved for longer than the Sessions
    /// threshold (half an hour).</summary>
    Stalled,

    /// <summary>Over; only its record is left.</summary>
    Finished
}

/// <summary>
/// What the Sessions record says about one session a task links to: where it stands
/// (<see cref="State"/>), and whether the Claude desktop app still holds it open.
/// </summary>
/// <param name="State">Running, stalled or finished, as the Sessions context decided.</param>
/// <param name="Archived">
/// Whether the desktop app has archived the session: true archived, false still open in
/// its sidebar, and null where there is no desktop record to say — a CLI-only or Copilot
/// session, or one from another machine. Null is not open
/// (<c>.devbook/domain/sessions/domain.md#session-view</c>). Orthogonal to
/// <paramref name="State"/>, not a fourth state: a running session can be archived too.
/// Read each time, never stored on the task.
/// </param>
public sealed record LinkedSession(LinkedSessionState State, bool? Archived);

/// <summary>
/// The state of the agent sessions a task links to, for the work badges that draw
/// them.
/// <para>
/// A port on Tasks' surface answered by an adapter over the Sessions context's
/// session source, the shape <see cref="IRoadmapTagSource"/> takes for the same
/// reason. A host that composes no Sessions answers nothing, and a badge with no
/// state draws as it did before it had one.
/// </para>
/// </summary>
public interface ILinkedSessionStates
{
    /// <summary>
    /// The state of each of <paramref name="sessionIds"/> the session record still
    /// holds, with whether the desktop app archived it, keyed by the id as asked
    /// (compared without regard to case). An id the record does not hold is absent
    /// rather than guessed. Never throws for an unreadable source: what could not be
    /// read is simply not in the answer.
    /// </summary>
    Task<IReadOnlyDictionary<string, LinkedSession>> StatesOfAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken = default);
}
