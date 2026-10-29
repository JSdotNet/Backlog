using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Resolves the Brain — the person's own notes, one folder outside every
/// repository (local ADR 0026) — as one devbook folder keyed
/// <see cref="BrainConventions.FolderKey"/>.
///
/// <para>It sits beside <see cref="DevbookFolderSource"/> rather than inside it
/// because the Brain shares none of a repository's machinery: no settings row per
/// folder, no clone or branch, no snapshot cache. It is always a local folder and
/// always editable, so the location it answers carries
/// <see cref="DevbookSourceKind.LocalFolder"/> and the panels offer the editor on
/// every note. Its folder is its own root, and the areas inside it are the ones its
/// <c>CONVENTIONS.md</c> declares, then the other top folders holding a note, minus
/// the ignored ones (<see cref="BrainConventions"/>) — the same set the database
/// builder reads under <see cref="DevbookCorpusKind.Brain"/>.</para>
///
/// <para>The Brain's folder is asked of <c>brainFolder</c> on every resolve, so a
/// setting that changes takes effect at once; whoever owns that setting calls
/// <see cref="NotifyContentChanged"/> when it does, and open panels reload. An
/// unset or missing folder is an ordinary answer — unavailable, with words that
/// say what to do — never an exception.</para>
///
/// <para>The Brain answers the unscoped question: no alias, or the empty alias
/// that the shell stores for the Brain scope (local ADR 0027 §5). A repository
/// alias is a repository's question, and this source has no folders for it.</para>
/// </summary>
public sealed class BrainFolderSource : IBrainFolderSource
{
    /// <summary>What the location says while no Brain folder is set.</summary>
    public const string NoBrainFolder = "Pick a folder for your Brain in Settings › Storage.";

    /// <summary>What the scope label of every Brain location reads.</summary>
    public const string ScopeLabel = BrainConventions.ScopeLabel;

    private readonly Func<string?> _brainFolder;

    public BrainFolderSource(Func<string?> brainFolder)
    {
        ArgumentNullException.ThrowIfNull(brainFolder);
        _brainFolder = brainFolder;
    }

    /// <summary>The one folder setting the Brain has — <see cref="BrainConventions.Folder"/>,
    /// defined there so the assistant tools, which cannot reference this project,
    /// name the same one.</summary>
    public static DevbookFolderSetting Folder => BrainConventions.Folder;

    public event Action? Changed;

    public void NotifyContentChanged() => Changed?.Invoke();

    /// <summary>The Brain's absolute folder, or null while none is set or the
    /// setting is not a usable path.</summary>
    public string? BrainRoot => RootFor(_brainFolder());

    /// <inheritdoc />
    public bool BrainFolderPresent() => BrainRoot is { } root && Directory.Exists(root);

    /// <summary>A configured folder as an absolute root, or null when it is blank,
    /// relative — which would resolve against wherever the process happens to
    /// run — or not a valid path.</summary>
    private static string? RootFor(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;

        try
        {
            var trimmed = configured.Trim();
            return Path.IsPathFullyQualified(trimmed) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed)) : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<BrainArea> Areas() => BrainConventions.Read(BrainRoot);

    /// <inheritdoc />
    public IReadOnlyList<BrainArea> HiddenAreas() => BrainConventions.Hidden(BrainRoot);

    /// <summary>The OneDrive conflict pairs among the Brain's notes, by name
    /// alone (<see cref="BrainConflictCopies"/>): a listing, never a read of a
    /// note and never a write. A dot-folder — <c>.obsidian</c>, <c>.trash</c> —
    /// holds no note and is not walked.</summary>
    public IReadOnlyList<BrainConflictCopy> ConflictCopies()
    {
        if (BrainRoot is not { } root || !Directory.Exists(root)) return [];

        try
        {
            return BrainConflictCopies.Find(Notes(root, root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed has no pair the pane could show.
            return [];
        }
    }

    private static IEnumerable<string> Notes(string root, string directory)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true };

        foreach (var file in Directory.EnumerateFiles(directory, "*.md", options))
        {
            yield return Path.GetRelativePath(root, file);
        }

        foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
        {
            if (Path.GetFileName(child).StartsWith('.')) continue;

            foreach (var note in Notes(root, child)) yield return note;
        }
    }

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
        IsBrainScope(repositoryAlias) ? [Folder] : [];

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!string.Equals(key, BrainConventions.FolderKey, StringComparison.OrdinalIgnoreCase))
        {
            return DevbookFolderLocation.Unavailable(key, $"The Brain has no {key} devbook folder.", scopeLabel: ScopeLabel);
        }

        if (!IsBrainScope(repositoryAlias))
        {
            return DevbookFolderLocation.Unavailable(key, $"{repositoryAlias} is a repository; its devbook is not the Brain.", scopeLabel: ScopeLabel);
        }

        var configured = _brainFolder();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DevbookFolderLocation.Unavailable(BrainConventions.FolderKey, NoBrainFolder, folder: Folder, scopeLabel: ScopeLabel);
        }

        if (RootFor(configured) is not { } root)
        {
            return DevbookFolderLocation.Unavailable(
                BrainConventions.FolderKey,
                $"The Brain folder path is not valid: {configured}",
                folder: Folder,
                scopeLabel: ScopeLabel);
        }

        var folder = Folder with { Path = root };
        if (!Directory.Exists(root))
        {
            return DevbookFolderLocation.Unavailable(
                BrainConventions.FolderKey,
                $"The Brain folder was not found at {root}.",
                folder: folder,
                fullPath: root,
                rootPath: root,
                scopeLabel: ScopeLabel);
        }

        // Recorded so a reader handed this folder opens the Brain's own database —
        // its folder is its root, not a folder one below one — and the refresher
        // builds it under the Brain's convention.
        DevbookDatabaseLocation.RegisterBrainRoot(root);

        return new DevbookFolderLocation(
            BrainConventions.FolderKey,
            true,
            null,
            null,
            folder,
            root,
            root,
            ScopeLabel,
            null,
            DevbookSourceKind.LocalFolder);
    }

    /// <summary>Whether <paramref name="repositoryAlias"/> names the Brain's
    /// scope: none, or the reserved empty value.</summary>
    private static bool IsBrainScope(string? repositoryAlias) => string.IsNullOrWhiteSpace(repositoryAlias);
}
