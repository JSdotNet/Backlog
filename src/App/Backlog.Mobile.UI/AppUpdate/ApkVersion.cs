using System.Globalization;
using System.Text.RegularExpressions;

namespace Backlog.Mobile.UI.AppUpdate;

/// <summary>
/// A release APK's version: what a person reads, and the versionCode Android
/// compares when one APK replaces another.
/// </summary>
/// <remarks>
/// The packing is release-mobile.yml's "Derive version numbers" step, digit for
/// digit: <c>major * 10^7 + minor * 10^5 + patch</c>, with the same limits. A
/// version that step would refuse to publish is one the phone refuses to read,
/// so the two cannot disagree about which release is newer. The code is a
/// <see cref="long"/> because Android's <c>PackageInfo.LongVersionCode</c> is, and
/// because the workflow's ceiling (214.99.99999) is just past <see cref="int.MaxValue"/>.
/// </remarks>
public sealed partial record ApkVersion(string Display, long VersionCode)
{
    public const int MaxMajor = 214;
    public const int MaxMinor = 99;
    public const int MaxPatch = 99999;

    /// <summary>The version a MAJOR.MINOR.PATCH string names, with its packed
    /// versionCode, or null when the workflow would refuse it: not MAJOR.MINOR.PATCH,
    /// past a limit, or zero.</summary>
    public static ApkVersion? TryParse(string? display)
    {
        if (display is null) return null;

        var match = SemanticVersion().Match(display);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return null;
        }

        if (major > MaxMajor || minor > MaxMinor || patch > MaxPatch) return null;

        var code = (major * 10_000_000L) + (minor * 100_000L) + patch;
        return code < 1 ? null : new ApkVersion(display, code);
    }

    /// <summary>
    /// The version in a release asset's name — <c>Backlog.Mobile_0.1.57.apk</c>, the
    /// name the workflow stages the signed APK under — or null for any other asset:
    /// the desktop's MSIX on a shared release, a bundle, or a name that does not parse.
    /// </summary>
    public static ApkVersion? TryParseAssetName(string? name)
    {
        if (name is null) return null;

        var match = AssetName().Match(name);
        return match.Success ? TryParse(match.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"^([0-9]+)\.([0-9]+)\.([0-9]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();

    [GeneratedRegex(@"^Backlog\.Mobile_([0-9]+\.[0-9]+\.[0-9]+)\.apk\z", RegexOptions.CultureInvariant)]
    private static partial Regex AssetName();
}
