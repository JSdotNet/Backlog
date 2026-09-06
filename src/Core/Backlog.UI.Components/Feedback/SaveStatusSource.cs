namespace Backlog.UI.Components.Feedback;

/// <summary>
/// Whatever the window is currently saving, said in the indicator's own
/// vocabulary. The shell's footer draws this; it does not know who fills it.
/// <para>
/// The indirection is the point. The save state belongs to a module — it is the
/// backlog's list that debounces, flushes and fails — and the band that reports it
/// belongs to the app shell, which under
/// <c>.arc42/adr/guidelines/0005-modular-monolith-structure.md</c> composes modules
/// rather than reading into them. A footer that asked a module's state class for
/// its own enum would be the shell knowing which module is the interesting one.
/// </para>
/// <para>
/// It speaks <see cref="SaveState"/>, the vocabulary
/// <c>.design/interaction-guidelines.md#save-state-indicator-vocabulary</c> fixes
/// and <c>SaveIndicator</c> draws, so the mapping from whatever a module calls its
/// own states happens once, inside that module.
/// </para>
/// </summary>
public interface ISaveStatusSource
{
    SaveState Current { get; }

    /// <summary>Raised when <see cref="Current"/> changes, and possibly off the
    /// renderer's thread — a debounce flush and a settle timer are both
    /// continuations.</summary>
    event Action? Changed;
}
