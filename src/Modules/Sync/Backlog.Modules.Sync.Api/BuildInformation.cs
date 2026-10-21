using System.Reflection;

namespace Backlog.Modules.Sync.Api;

/// <summary>
/// Which build of the service is running, read once off the entry assembly.
///
/// <para>The SDK writes <c>&lt;version&gt;+&lt;sha&gt;</c> into
/// <see cref="AssemblyInformationalVersionAttribute"/> whenever it builds from a
/// git checkout — the source-control targets it ships resolve <c>HEAD</c> into
/// <c>SourceRevisionId</c> and the assembly-info target appends it — so the
/// container <c>azd deploy</c> publishes carries the commit it was cut from
/// without a build step of its own. A build with no checkout behind it carries
/// the bare version, and then the commit is empty rather than invented: a
/// machine deciding whether a deploy is due compares this against the
/// repository, and a made-up value could compare equal.</para>
/// </summary>
internal static class BuildInformation
{
    private static readonly string Informational =
        typeof(BuildInformation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;

    /// <summary>The version half: <c>1.0.0</c> out of <c>1.0.0+8315afc…</c>.</summary>
    public static string Version { get; } = VersionOf(Informational);

    /// <summary>The commit half — the full sha the build was cut from, or empty
    /// when it was not stamped.</summary>
    public static string Commit { get; } = CommitOf(Informational);

    /// <summary>The release number the deploy stamped (<c>0.1.123</c>), or null for a
    /// build no release stamped. Kept apart from <see cref="Version"/>: that is the
    /// compatibility number clients stop syncing above, and a release must never
    /// move it.</summary>
    public static string? Release { get; } =
        ReleaseOf(typeof(BuildInformation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>());

    /// <summary>The <c>BacklogRelease</c> assembly metadata, which the project writes
    /// from the MSBuild property of that name; null when it is absent or blank.</summary>
    public static string? ReleaseOf(IEnumerable<AssemblyMetadataAttribute> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var value = metadata.FirstOrDefault(entry => entry.Key == ReleaseMetadataKey)?.Value?.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The metadata key, and the MSBuild property, that carry the release.</summary>
    public const string ReleaseMetadataKey = "BacklogRelease";

    public static string VersionOf(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return (plus < 0 ? informational : informational[..plus]).Trim();
    }

    public static string CommitOf(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? string.Empty : informational[(plus + 1)..].Trim();
    }
}
