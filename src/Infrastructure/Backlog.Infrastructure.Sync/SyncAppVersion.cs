using System.Reflection;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The version of the app this device is running, which
/// <see cref="SyncCompatibility"/> compares with the sync service's.
/// <para>
/// A value the head hands in rather than one read here, because only the head
/// knows which number is the real one: a packaged desktop is its MSIX package
/// version, a phone is its APK's, and the entry assembly says <c>1.0.0</c> for
/// both. Each head registers the version its update window shows, through
/// <c>AddSyncAppVersion</c>; a head that registers none gets
/// <see cref="OfEntryAssembly"/>.
/// </para>
/// </summary>
public sealed class SyncAppVersion
{
    public SyncAppVersion(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>As the head reports it — <c>0.1.122.0</c>, <c>0.1.63</c>, <c>1.0</c>.</summary>
    public string Value { get; }

    /// <summary>The entry assembly's informational version, the fallback for a
    /// head that names none. Its <c>+commit</c> suffix is left on: the comparison
    /// ignores it. The same read as <c>Backlog.UI.Components.AppVersion.OfEntryAssembly</c>,
    /// repeated because an adapter may not reference the UI library.</summary>
    public static SyncAppVersion OfEntryAssembly()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SyncAppVersion).Assembly;

        return new SyncAppVersion(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? string.Empty);
    }

    public override string ToString() => Value;
}
