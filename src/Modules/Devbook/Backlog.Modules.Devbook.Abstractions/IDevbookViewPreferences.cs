namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// How this reader last had the Devbook pane drawn, where a choice about the view has to
/// outlive the pane. Per device and never synced, the same footing as the roadmap's
/// view choices (local ADR 0019, §4), answered by the host over its per-device settings
/// file.
/// <para>
/// A port on this context's own surface because a module may not reference
/// infrastructure (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>).
/// The pane resolves it optionally: a host that composes none holds the choice for the
/// life of the pane and asks again next time.
/// </para>
/// </summary>
public interface IDevbookViewPreferences
{
    /// <summary>Whether the reader dismissed the ways-to-move hint the C4 zoom tab shows
    /// the first time it opens.</summary>
    bool C4ZoomHintDismissed { get; }

    /// <summary>Remembers the hint as dismissed on this device.</summary>
    void DismissC4ZoomHint();
}
