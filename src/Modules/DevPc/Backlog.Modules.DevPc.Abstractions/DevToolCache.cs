using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// One version of a plugin sitting in Claude's plugin cache.
///
/// <para>Claude installs a marketplace plugin by cloning it into
/// <c>~/.claude/plugins/cache/&lt;marketplace&gt;/&lt;plugin&gt;/&lt;version&gt;/</c>
/// and pointing the install at that folder. An update writes the next version
/// beside the last one and moves the pointer; nothing ever removes the folder it
/// moved off. So the cache grows one folder per version ever installed, and a
/// project-scoped install made months ago still points at the folder it was made
/// against — which is how a machine that says 1.0.1 everywhere keeps running 1.0.0
/// in one repository.</para>
///
/// <para><see cref="Installs"/> is how many installs, at any scope, point at this
/// folder. Zero is a folder nothing loads any more and the one this pane offers to
/// delete; anything else is a folder deleting would break an install, and the fix
/// for an old version that is still in use is an update, not a delete.</para>
/// </summary>
public sealed record DevToolCachedVersion(string Version, string Path, int Installs)
{
    /// <summary>Whether some install still points here.</summary>
    public bool InUse => Installs > 0;
}

/// <summary>
/// What is known about the layout of Claude's plugin cache, and how a listing of
/// it is read against what Claude has installed.
///
/// <para>Pure, and beside the catalog format for the reason the CLI parsers are:
/// a test can hand it the folder names and the install paths and pin the answer
/// without a cache on disk. The desktop adapter owns the file system walk and the
/// delete.</para>
/// </summary>
public static class DevToolCache
{
    /// <summary>Where Claude keeps its plugin clones, under its config folder.</summary>
    public static string CacheRoot(string claudeConfigDirectory) =>
        System.IO.Path.Combine(claudeConfigDirectory, "plugins", "cache");

    /// <summary>The folder every cached version of one plugin sits under:
    /// <c>&lt;root&gt;/&lt;marketplace&gt;/&lt;name&gt;</c>, out of the plugin's
    /// <c>name@marketplace</c> id.</summary>
    /// <returns>Null for an id with no marketplace half — there is no folder to
    /// name for it, and inventing one would be a walk of the wrong place.</returns>
    public static string? PluginDirectory(string cacheRoot, string pluginId)
    {
        var at = pluginId.LastIndexOf('@');
        if (at <= 0 || at == pluginId.Length - 1)
        {
            return null;
        }

        var name = pluginId[..at].Trim();
        var marketplace = pluginId[(at + 1)..].Trim();

        // Both halves are folder names and nothing else. An id is what the catalog
        // spelled, and a catalog is a hand-edited file: one carrying a separator
        // would walk — or delete — outside the plugin's own folder.
        return IsPlainFolderName(name) && IsPlainFolderName(marketplace)
            ? System.IO.Path.Combine(cacheRoot, marketplace, name)
            : null;
    }

    /// <summary>Whether a version, as a person or a row spelled it, names one
    /// folder directly under a plugin's cache folder and nothing beyond it.</summary>
    public static bool IsPlainFolderName(string value) =>
        value.Length > 0
        && value is not "." and not ".."
        && value.IndexOfAny(['/', '\\', ':']) < 0
        && value.Trim() == value;

    /// <summary>
    /// The versions in one plugin's cache folder, each with how many installs
    /// point at it, oldest first.
    /// </summary>
    /// <param name="versionDirectories">Every folder directly under the plugin's
    /// cache folder. The leaf name is the version.</param>
    /// <param name="installPaths">What <see cref="DevToolOutput.ParseClaudePluginInstallPaths"/>
    /// read: every install path Claude reports, keyed the way <see cref="NormalizePath"/>
    /// spells it, with how many installs share it.</param>
    public static IReadOnlyList<DevToolCachedVersion> Describe(
        IEnumerable<string> versionDirectories,
        IReadOnlyDictionary<string, int> installPaths)
    {
        return versionDirectories
            .Select(directory => new DevToolCachedVersion(
                LeafName(directory),
                directory,
                installPaths.GetValueOrDefault(NormalizePath(directory))))
            .OrderBy(cached => cached.Version, VersionOrder.Instance)
            .ToArray();
    }

    /// <summary>The last segment of a path under either separator. Claude writes
    /// backslashes on Windows, and <c>Path.GetFileName</c> on any other platform
    /// would hand the whole path back as the version.</summary>
    private static string LeafName(string directory)
    {
        var trimmed = directory.TrimEnd('/', '\\');
        var cut = trimmed.LastIndexOfAny(['/', '\\']);
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }

    /// <summary>
    /// One spelling for a path, so a folder the walk found and a path Claude
    /// printed compare as the same place.
    ///
    /// <para>Claude writes <c>C:\Users\...</c> with backslashes into its listing and
    /// a walk on the same machine hands back whichever separator the framework
    /// chose; case differs between the two on Windows as well. Neither is a
    /// different folder.</para>
    /// </summary>
    public static string NormalizePath(string path) =>
        path.Trim().Replace('\\', '/').TrimEnd('/').ToUpperInvariant();

    /// <summary>Numeric where both sides parse as versions, so 0.9.0 sorts before
    /// 0.11.0; ordinal otherwise, so a folder named anything else still lands
    /// somewhere stable.</summary>
    private sealed class VersionOrder : IComparer<string>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            if (Version.TryParse(x, out var left) && Version.TryParse(y, out var right))
            {
                return left.CompareTo(right);
            }

            return string.CompareOrdinal(x, y);
        }
    }
}
