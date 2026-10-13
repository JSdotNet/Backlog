using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Resolves the Brain — the person's own notes, one folder outside every
/// repository (local ADR 0026) — as one knowledge folder keyed
/// <see cref="BrainConventions.FolderKey"/>.
///
/// <para>It sits beside <see cref="DevbookFolderSource"/> rather than inside it
/// because the Brain shares none of a repository's machinery: no settings row per
/// folder, no clone or branch, no snapshot cache. It is always a local folder and
/// always editable, so the location it answers carries
/// <see cref="DevbookSourceKind.LocalFolder"/> and the panels offer the editor on
/// every note. Its folder is its own root, and the areas inside it are the ones its
/// <c>CONVENTIONS.md</c> declares (<see cref="BrainConventions"/>), read by the
/// database builder under <see cref="DevbookCorpusKind.Brain"/>.</para>
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
public sealed class BrainFolderSource : IDevbookFolderSource
{
    /// <summary>What the location says while no Brain folder is set.</summary>
    public const string NoBrainFolder = "Pick a folder for your Brain in Settings › Storage.";

    /// <summary>What the scope label of every Brain location reads.</summary>
    public const string ScopeLabel = "Brain";

    private readonly Func<string?> _brainFolder;

    public BrainFolderSource(Func<string?> brainFolder)
    {
        ArgumentNullException.ThrowIfNull(brainFolder);
        _brainFolder = brainFolder;
    }

    /// <summary>The one folder setting the Brain has.</summary>
    public static DevbookFolderSetting Folder { get; } =
        new(BrainConventions.FolderKey, ScopeLabel, string.Empty, SupportsPathOverride: false);

    public event Action? Changed;

    public void NotifyContentChanged() => Changed?.Invoke();

    /// <summary>The Brain's absolute folder, or null while none is set or the
    /// setting is not a usable path.</summary>
    public string? BrainRoot => RootFor(_brainFolder());

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

    /// <summary>The areas the Brain shows, in order: what its
    /// <c>CONVENTIONS.md</c> declares, or the PARA set.</summary>
    public IReadOnlyList<BrainArea> Areas() => BrainConventions.Read(BrainRoot);

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) =>
        IsBrainScope(repositoryAlias) ? [Folder] : [];

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!string.Equals(key, BrainConventions.FolderKey, StringComparison.OrdinalIgnoreCase))
        {
            return DevbookFolderLocation.Unavailable(key, $"The Brain has no {key} knowledge folder.", scopeLabel: ScopeLabel);
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
