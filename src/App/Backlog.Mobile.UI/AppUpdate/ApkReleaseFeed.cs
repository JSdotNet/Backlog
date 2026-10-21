using System.Text.Json;

namespace Backlog.Mobile.UI.AppUpdate;

/// <summary>A published mobile APK: its version, the release it is on, and where to download it.</summary>
/// <param name="Version">The version the asset's name carries.</param>
/// <param name="Tag">The release's tag — <c>v1.2.3</c>, or <c>mobile-v0.1.N</c> for a nightly published before Release All aligned the two channels.</param>
/// <param name="DownloadUrl">The asset's <c>browser_download_url</c>, exactly as GitHub returned it.</param>
/// <param name="Size">The asset's size in bytes as GitHub recorded it: the download may not exceed it.</param>
public sealed record ApkRelease(ApkVersion Version, string Tag, Uri DownloadUrl, long Size);

/// <summary>
/// Picks the APK to offer out of GitHub's "list releases" answer.
/// </summary>
/// <remarks>
/// <para>
/// <c>/releases/latest</c> cannot answer this. It points at the release flagged latest,
/// which Release All keeps for the desktop's App Installer: a nightly stays off it while a
/// higher version holds it, and older <c>v*</c> releases carry no APK at all. The mobile
/// nightlies before it were published with <c>--latest=false</c> under their own
/// <c>mobile-v*</c> tags, and phones still run them. So every non-draft release is read,
/// prerelease or not, and the asset whose name packs to the highest versionCode wins,
/// whichever tag namespace it came from.
/// </para>
/// <para>
/// Only an https download from github.com is offered: the URL the phone fetches the
/// package from is the one GitHub returned for that asset, and nothing else. Android
/// checks the signature at install, so a package that is not ours cannot replace the
/// app; this keeps the phone from fetching one in the first place.
/// </para>
/// </remarks>
public static class ApkReleaseFeed
{
    /// <summary>
    /// Reads a release list and picks the newest mobile APK in it, or null when there
    /// is none. False when <paramref name="json"/> is not one — not JSON, or
    /// JSON that is not an array, such as the object GitHub answers a rate limit with —
    /// so a caller can tell "unreadable" from "no APK published". Never throws.
    /// </summary>
    public static bool TryRead(string json, out ApkRelease? newest)
    {
        newest = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;

            foreach (var release in document.RootElement.EnumerateArray())
            {
                foreach (var candidate in ApksOf(release))
                {
                    if (newest is null || candidate.Version.VersionCode > newest.Version.VersionCode)
                    {
                        newest = candidate;
                    }
                }
            }

            return true;
        }
        catch (JsonException)
        {
            newest = null;
            return false;
        }
    }

    private static IEnumerable<ApkRelease> ApksOf(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object) yield break;
        if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) yield break;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) yield break;

        var tag = StringOf(release, "tag_name") ?? string.Empty;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;

            // "starter" is an upload that has not finished; its URL answers 404.
            var state = StringOf(asset, "state");
            if (state is not null && state != "uploaded") continue;

            var version = ApkVersion.TryParseAssetName(StringOf(asset, "name"));
            if (version is null) continue;

            if (!Uri.TryCreate(StringOf(asset, "browser_download_url"), UriKind.Absolute, out var url)) continue;
            if (url.Scheme != Uri.UriSchemeHttps || !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)) continue;

            // The size bounds the download, so an asset that does not state one is not offered.
            if (!asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number
                || !size.TryGetInt64(out var bytes) || bytes <= 0) continue;

            yield return new ApkRelease(version, tag, url, bytes);
        }
    }

    private static string? StringOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
