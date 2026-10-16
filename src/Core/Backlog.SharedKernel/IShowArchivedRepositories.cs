namespace Backlog.SharedKernel;

/// <summary>
/// Show archived (local ADR 0029, §3): the person's choice, kept on this device and never
/// synced, to see archived repositories where they are otherwise hidden — the header's
/// repository scope, the Board's columns and the repository pickers. It changes what is
/// shown, never what is archived.
/// <para>
/// In the shared kernel beside <see cref="BrainScope"/>, because the scope it narrows is
/// every module's and a module may not reference infrastructure. The host answers it over
/// its per-device shell navigation file; a host that composes none leaves the readers on
/// the default, off.
/// </para>
/// </summary>
public interface IShowArchivedRepositories
{
    /// <summary>Whether archived repositories are shown: off until turned on on this
    /// device.</summary>
    bool Shown { get; }

    /// <summary>Remembers the choice on this device. Raises <see cref="Changed"/> only
    /// when it is a change.</summary>
    void SetShown(bool shown);

    /// <summary>Raised after <see cref="Shown"/> changed, and only then.</summary>
    event Action? Changed;
}
