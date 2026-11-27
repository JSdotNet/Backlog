using Backlog.Modules.Repositories.Abstractions.Environments;

namespace Backlog.Modules.Repositories.Abstractions.Services;

/// <summary>
/// The registry's named environments (<c>domain/repositories</c>, local ADR 0073 §3):
/// each registered repository's own, and the no-repository group's. Every edit is saved
/// as it is made, the house rule of no save button, and answers null or the reason it
/// was refused.
/// <para>
/// An environment keeps its home, name, type and launch link for its whole life; the
/// arranging edits — pin, hide, move — change nothing else. Archiving or removing a
/// repository never moves its environments to the no-repository group.
/// </para>
/// </summary>
public interface IEnvironments
{
    /// <summary>What an edit answers when it was made but could not be kept for the
    /// next launch: the edit holds for this session.</summary>
    const string KeptNotSaved = "Changed, but the environments couldn't be saved for next time.";

    /// <summary>What every edit answers while the store could not be read at start, so
    /// a write never replaces a file that is there and simply unreadable.</summary>
    const string StoreUnreadable = "The environments couldn't be read, so they can't be changed right now.";

    /// <summary>Raised after any edit, here or on the store underneath.</summary>
    event Action? Changed;

    /// <summary>The environments of <paramref name="home"/>: pinned ones first, then the
    /// rest, each group in its order. Hidden ones are left out unless
    /// <paramref name="includeHidden"/>.</summary>
    IReadOnlyList<RepositoryEnvironment> For(EnvironmentHome home, bool includeHidden = false);

    /// <summary>How many of <paramref name="home"/>'s environments are hidden.</summary>
    int HiddenCount(EnvironmentHome home);

    /// <summary>Adds an environment at the end of <paramref name="home"/>'s list.</summary>
    string? Add(EnvironmentHome home, string name, EnvironmentType type, LaunchLink link, IEnumerable<string>? accessHints = null);

    string? SetPinned(EnvironmentHome home, Guid id, bool pinned);

    string? SetHidden(EnvironmentHome home, Guid id, bool hidden);

    /// <summary>Moves an environment one place earlier (<paramref name="offset"/> −1) or
    /// later (+1) among its home's environments.</summary>
    string? Move(EnvironmentHome home, Guid id, int offset);

    string? Remove(EnvironmentHome home, Guid id);
}

/// <summary>
/// Where the environments are kept: per device, beside the registry's own per-device
/// file. The module holds the rules; this port only reads and writes the whole list.
/// </summary>
public interface IEnvironmentStore
{
    /// <summary>Raised when what <see cref="Load"/> answers changed underneath the module.</summary>
    event Action? Changed;

    /// <summary>Every environment, each home's in its order; null when the store could
    /// not be read, which is not the same answer as no environments.</summary>
    IReadOnlyList<StoredEnvironment>? Load();

    /// <summary>Writes the whole list. Answers null, or why it could not be kept for the
    /// next launch.</summary>
    string? Save(IReadOnlyList<StoredEnvironment> environments);
}

/// <summary>
/// Runs an environment's command on this device. A port because how a command is started
/// is local-machine detail, answered in the file-system adapter.
/// </summary>
public interface IEnvironmentCommandRunner
{
    /// <summary>Starts <paramref name="commandLine"/> in a window of its own. Answers null,
    /// or why it was not started.</summary>
    string? Run(string commandLine, string? workingDirectory);
}
