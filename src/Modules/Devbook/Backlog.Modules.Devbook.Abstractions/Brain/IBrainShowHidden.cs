namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Show hidden (local ADR 0026 §2): the person's choice, kept on this device and
/// never synced, to see the Brain's hidden folders — the ones its
/// <c>CONVENTIONS.md</c> lists under <c>Ignore</c> and its dot-folders — as tabs
/// marked hidden after every area, with each area's dot-entries in its menu. It
/// changes what the pane shows, never which folders are areas, so search and the
/// Brain database still leave them out.
/// <para>
/// Not in the Brain folder, which another machine reads, and not on
/// <see cref="IBrainSettings"/>, whose change reloads the Brain: the host answers
/// it over its per-device shell navigation file, as it answers show archived. A
/// host that composes none leaves the pane on the default, off.
/// </para>
/// </summary>
public interface IBrainShowHidden
{
    /// <summary>Whether the hidden folders are shown: off until turned on on this
    /// device.</summary>
    bool Shown { get; }

    /// <summary>Remembers the choice on this device. Raises <see cref="Changed"/>
    /// only when it is a change.</summary>
    void SetShown(bool shown);

    /// <summary>Raised after <see cref="Shown"/> changed, and only then.</summary>
    event Action? Changed;
}
