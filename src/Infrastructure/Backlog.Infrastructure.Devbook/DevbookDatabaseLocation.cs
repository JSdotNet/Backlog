using System.Security.Cryptography;
using System.Text;

using Backlog.Infrastructure.Devbook.Building;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// Where a repository's devbook database is: in the app's own storage, one per
/// repository path, never inside a repository (local ADR 0015).
///
/// <code>
/// &lt;devbook cache folder&gt;/_databases/&lt;name&gt;-&lt;hash&gt;/devbook.db
/// </code>
///
/// <para>The devbook cache folder is the one the branch snapshots of local
/// ADR 0008 use, handed over by the composition root through
/// <see cref="Configure"/> so that moving it on the Storage settings tab moves
/// the databases too. <c>_databases</c> cannot collide with a snapshot folder,
/// which is <c>&lt;owner&gt;-&lt;name&gt;</c> and a GitHub owner cannot start
/// with an underscore. The key is the repository folder's name, for a person
/// browsing the folder, and a hash of its normalized absolute path, so two
/// worktrees of one repository — two sets of files — get two databases.</para>
///
/// <para>Static because the callers are: a knowledge consumer is handed a
/// <i>folder</i> path, often from inside a static reader with a static cache, and
/// this is the one place a folder becomes a database. Until something configures
/// it there is no storage and every answer is <see langword="null"/> — no
/// database, so every reader takes the Markdown path, which is the ladder's floor
/// and correct.</para>
///
/// <para>Asking for a repository's database is also the moment its database is
/// wanted, so a configured observer hears about each ask (see
/// <see cref="DevbookDatabaseRefresher"/>). The ask never waits for it.</para>
///
/// <para>A folder outside every repository — the Brain's, local ADR 0026 — gets
/// its database the same way, keyed by its absolute path under the same
/// <c>_databases</c> folder, and never in the folder itself, which is often
/// synced. What differs is that such a folder is the root rather than a folder
/// one step below it, and that it is read under its own corpus profile;
/// <see cref="RegisterCorpus"/> records both, so a reader handed the folder and
/// the refresher building it each know it for what it is.</para>
/// </summary>
public static class DevbookDatabaseLocation
{
    /// <summary>The database's file name.</summary>
    public const string FileName = "devbook.db";

    /// <summary>The folder under the devbook cache folder that holds one folder
    /// per repository path.</summary>
    public const string DatabasesFolderName = "_databases";

    /// <summary>The folder beside <see cref="DatabasesFolderName"/> that holds the
    /// Archify artifacts the app draws on demand from committed specifications
    /// (local ADR 0034). Underscore-prefixed for the same reason: no snapshot
    /// folder can be called that.</summary>
    public const string ArchifyRendersFolderName = "_archify-renders";

    private const int HashLength = 12;

    private const int NameLength = 40;

    private static volatile Configuration? s_configuration;

    /// <summary>Which normalized root each corpus profile is registered for, at
    /// most one per profile: replaced whole under <see cref="s_corporaGate"/>, read
    /// without it. A newly registered root replaces the profile's last — a moved
    /// setting must not leave its old folder read under the profile.</summary>
    private static volatile CorpusRoot[] s_corpora = [];

    private static readonly Lock s_corporaGate = new();

    /// <summary>
    /// Points the resolver at the app's devbook cache folder, read on every ask so
    /// a changed setting takes effect at once, and names who to tell when a
    /// repository's database is asked for. Called once by each composition root;
    /// a later call replaces the earlier one.
    /// </summary>
    public static void Configure(Func<string?> devbookCacheDirectory, Action<string>? requested = null)
    {
        ArgumentNullException.ThrowIfNull(devbookCacheDirectory);
        s_configuration = new Configuration(devbookCacheDirectory, requested);
    }

    /// <summary>The folder databases are kept in, or <see langword="null"/> while
    /// nothing has configured one.</summary>
    public static string? DatabasesDirectory =>
        s_configuration?.CacheDirectory() is { Length: > 0 } cache ? Path.Combine(cache, DatabasesFolderName) : null;

    /// <summary>The folder Archify renders are cached in, beside the databases, or
    /// <see langword="null"/> while nothing has configured the devbook cache
    /// folder. Read on every ask, so a moved Storage setting moves it too.</summary>
    public static string? ArchifyRendersDirectory =>
        s_configuration?.CacheDirectory() is { Length: > 0 } cache ? Path.Combine(cache, ArchifyRendersFolderName) : null;

    /// <summary>
    /// The database for the repository at <paramref name="repositoryRoot"/>,
    /// whether or not it exists yet, or <see langword="null"/> when there is no
    /// repository or no configured storage. Tells the configured observer.
    /// </summary>
    public static string? ForRepositoryRoot(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return null;

        var configuration = s_configuration;
        if (configuration?.CacheDirectory() is not { Length: > 0 } cache) return null;

        var path = PathFor(Path.Combine(cache, DatabasesFolderName), repositoryRoot);
        if (path is not null) configuration.Requested?.Invoke(repositoryRoot);

        return path;
    }

    /// <summary>
    /// The database for the repository a devbook folder belongs to. The
    /// repository root is one level up from a root-layout folder (<c>.arc42</c>)
    /// and two from a devbook-layout one (<c>.devbook/arc42</c>). A folder
    /// registered under a corpus profile (<see cref="RegisterCorpus"/>) is its own
    /// root.
    /// </summary>
    public static string? ForDevbookFolder(string? devbookFolderPath) =>
        IsRegisteredRoot(devbookFolderPath)
            ? ForRepositoryRoot(devbookFolderPath)
            : RepositoryRootFor(devbookFolderPath) is { } root ? ForRepositoryRoot(root) : null;

    /// <summary>
    /// Records <paramref name="root"/> as the folder read under
    /// <paramref name="profile"/>, in place of the root it was registered for
    /// before and of any other profile registered for the same root, without
    /// asking for its database. Null or blank clears the profile's root.
    /// <see cref="DevbookCorpusProfile.Repository"/> is what every unregistered
    /// root reads as, so it is never registered.
    /// </summary>
    public static void RegisterCorpus(DevbookCorpusProfile profile, string? root)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (ReferenceEquals(profile, DevbookCorpusProfile.Repository))
        {
            throw new ArgumentException("The repository profile is what an unregistered root reads as; it is never registered.", nameof(profile));
        }

        var normalized = root is null ? null : NormalizedRoot(root);

        lock (s_corporaGate)
        {
            var kept = s_corpora.Where(corpus => !ReferenceEquals(corpus.Profile, profile)
                && !string.Equals(corpus.Root, normalized, StringComparison.Ordinal));

            s_corpora = normalized is null ? [.. kept] : [.. kept, new CorpusRoot(profile, normalized)];
        }
    }

    /// <summary>The profile the folder at <paramref name="root"/> is built under:
    /// the one it was registered for, or <see cref="DevbookCorpusProfile.Repository"/>.</summary>
    public static DevbookCorpusProfile ProfileFor(string? root)
    {
        if (root is null || NormalizedRoot(root) is not { } normalized) return DevbookCorpusProfile.Repository;

        foreach (var corpus in s_corpora)
        {
            if (string.Equals(corpus.Root, normalized, StringComparison.Ordinal)) return corpus.Profile;
        }

        return DevbookCorpusProfile.Repository;
    }

    /// <summary>Whether <paramref name="path"/> is a root registered under a
    /// profile other than the repository's.</summary>
    public static bool IsRegisteredRoot(string? path) =>
        !ReferenceEquals(ProfileFor(path), DevbookCorpusProfile.Repository);

    /// <summary>The repository root above a devbook folder, or
    /// <see langword="null"/> when the path has no parent to go to.</summary>
    public static string? RepositoryRootFor(string? devbookFolderPath)
    {
        if (string.IsNullOrWhiteSpace(devbookFolderPath)) return null;

        try
        {
            var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(devbookFolderPath)));
            if (parent is null) return null;

            var root = string.Equals(parent.Name, DevbookFolderSetting.DevbookRoot, StringComparison.OrdinalIgnoreCase) && parent.Parent is not null
                ? parent.Parent
                : parent;

            return root.FullName;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The database for <paramref name="repositoryRoot"/> under
    /// <paramref name="databasesDirectory"/>. Pure: no configuration, no observer,
    /// no disk.
    /// </summary>
    public static string? PathFor(string databasesDirectory, string repositoryRoot) =>
        KeyFor(repositoryRoot) is { } key ? Path.Combine(databasesDirectory, key, FileName) : null;

    /// <summary>
    /// A repository's folder name under <c>_databases</c>: its root folder's name,
    /// made path-safe and shortened (lower-case on Windows, from the normalized
    /// path), then the first twelve hexadecimal characters
    /// of the SHA-256 of its normalized absolute path.
    /// </summary>
    public static string? KeyFor(string repositoryRoot)
    {
        if (NormalizedRoot(repositoryRoot) is not { } normalized) return null;

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..HashLength];
        // The name comes from the normalized path too, so one repository spelled
        // in two casings on Windows is one key rather than two.
        var name = SafeName(Path.GetFileName(normalized));

        return name.Length == 0 ? hash : $"{name}-{hash}";
    }

    /// <summary>
    /// The path a key is taken over: absolute, without a trailing separator, and
    /// lower-cased where the file system compares paths case-insensitively — so
    /// <c>D:\Repos\Backlog\</c> and <c>d:\repos\backlog</c> are one repository and
    /// get one database.
    /// </summary>
    public static string? NormalizedRoot(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return null;

        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string SafeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name) builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');

        var safe = builder.ToString().Trim('-', '.');
        return safe.Length > NameLength ? safe[..NameLength].TrimEnd('-', '.') : safe;
    }

    private sealed record Configuration(Func<string?> CacheDirectory, Action<string>? Requested);

    private sealed record CorpusRoot(DevbookCorpusProfile Profile, string Root);
}
