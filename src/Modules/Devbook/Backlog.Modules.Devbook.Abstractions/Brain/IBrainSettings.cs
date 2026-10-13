namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Where the person's Brain lives: the one folder per user, outside every
/// repository, that local ADR 0026 decided.
/// <para>
/// No default, by that decision's own *Rejected* list: no path suits every person,
/// and a guessed one may land inside a repository or outside the synced folder. So
/// <see cref="BrainFolder"/> is null until somebody picks one, and every reader of
/// it treats null as "the Brain is not set up yet" rather than as an error.
/// </para>
/// <para>
/// The path is kept in the app's per-user settings, beside the workspace settings,
/// and never in the folder it names: the app has to know the path before it can
/// read the folder. The setting is <c>brain-folder</c> in
/// <c>.devbook/domain/devbook/context.md</c>.
/// </para>
/// </summary>
public interface IBrainSettings
{
    /// <summary>The Brain folder's full path, or null while none is set.</summary>
    string? BrainFolder { get; }

    /// <summary>Whether a Brain folder has been set.</summary>
    bool IsSet => BrainFolder is not null;

    /// <summary>Points the Brain at <paramref name="path"/>, or clears it when the
    /// path is blank. Answers why the path cannot be used rather than throwing: a
    /// path typed into a settings field is an ordinary thing to get wrong.</summary>
    string? TrySetBrainFolder(string? path);

    /// <summary>Raised after <see cref="BrainFolder"/> changes, so the chip that
    /// reads <see cref="BrainSetup.SetUpLabel"/> and the Devbook pane on the Brain
    /// can follow without a restart.</summary>
    event Action? Changed;
}
