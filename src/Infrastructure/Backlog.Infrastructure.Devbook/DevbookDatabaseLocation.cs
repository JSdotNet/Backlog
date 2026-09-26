using System.Security.Cryptography;
using System.Text;

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
/// </summary>
public static class DevbookDatabaseLocation
{
    /// <summary>The database's file name.</summary>
    public const string FileName = "devbook.db";

    /// <summary>The folder under the devbook cache folder that holds one folder
    /// per repository path.</summary>
    public const string DatabasesFolderName = "_databases";

    private const int HashLength = 12;

    private const int NameLength = 40;

    private static volatile Configuration? s_configuration;

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
    /// The database for the repository a knowledge folder belongs to. The
    /// repository root is one level up from a root-layout folder (<c>.arc42</c>)
    /// and two from a devbook-layout one (<c>.devbook/arc42</c>).
    /// </summary>
    public static string? ForDevbookFolder(string? devbookFolderPath) =>
        RepositoryRootFor(devbookFolderPath) is { } root ? ForRepositoryRoot(root) : null;

    /// <summary>The repository root above a knowledge folder, or
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
    internal static string? NormalizedRoot(string repositoryRoot)
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
}
