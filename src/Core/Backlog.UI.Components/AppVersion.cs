using System.Reflection;

namespace Backlog.UI.Components;

/// <summary>
/// Formats a version for display, favouring the informational version (which
/// carries the semantic version stamped at build time) and falling back to the
/// assembly version.
/// <para>
/// It lives in the shared library rather than beside the update service that
/// first needed it, because four hosts now show a version and only two of them
/// can see that service: the desktop head and its web harness reach it through
/// <c>Backlog.Desktop.UI</c>, while the storybook and the mobile harness show
/// theirs in a footer and are allowed no such reference. This is the one
/// assembly all four already share.
/// </para>
/// </summary>
public static class AppVersion
{
    /// <summary>The display version of the entry (or this) assembly.</summary>
    public static string OfEntryAssembly() =>
        Of(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    /// <summary>The display version of a specific assembly.</summary>
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return Normalize(informational)
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    /// <summary>
    /// Trims the source-revision suffix the SDK appends to the informational
    /// version (e.g. <c>1.2.3+abc1234</c> becomes <c>1.2.3</c>) and rejects
    /// blank values.
    /// </summary>
    public static string? Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var plus = informationalVersion.IndexOf('+');
        var trimmed = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        trimmed = trimmed.Trim();

        return trimmed.Length == 0 ? null : trimmed;
    }
}
