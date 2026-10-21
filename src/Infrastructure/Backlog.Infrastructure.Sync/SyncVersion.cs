using System.Globalization;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Whether the sync service's version is newer than this app's.
/// <para>
/// Not <see cref="Version"/>, because that type says <c>1.0</c> is older than
/// <c>1.0.0</c>: a part it was not given is -1, not 0. The two sides here come
/// from different places — an MSIX package's four parts, an APK's three, a
/// development build's <c>1.0</c> — so a missing part has to count as zero or a
/// development build would be told to update against a service it matches.
/// </para>
/// </summary>
internal static class SyncVersion
{
    private const int MaximumParts = 4;

    /// <summary>True only when both read as versions and the service's is
    /// strictly greater. A version either side cannot read is not newer: sync
    /// goes on as it did before the check existed.</summary>
    public static bool IsServiceNewer(string? service, string? app) =>
        TryParse(service, out var serviceParts)
        && TryParse(app, out var appParts)
        && Compare(serviceParts, appParts) > 0;

    /// <summary>Up to four dot-separated non-negative numbers, read after
    /// dropping anything from the first <c>-</c> or <c>+</c> — a prerelease
    /// label or the commit the SDK appends.</summary>
    internal static bool TryParse(string? text, out int[] parts)
    {
        parts = new int[MaximumParts];

        if (string.IsNullOrWhiteSpace(text)) return false;

        var core = text.Trim();
        var suffix = core.IndexOfAny(['-', '+']);
        if (suffix >= 0) core = core[..suffix];

        var pieces = core.Split('.');
        if (pieces.Length > MaximumParts) return false;

        for (var index = 0; index < pieces.Length; index++)
        {
            if (!int.TryParse(pieces[index], NumberStyles.None, CultureInfo.InvariantCulture, out parts[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int Compare(int[] left, int[] right)
    {
        for (var index = 0; index < MaximumParts; index++)
        {
            var order = left[index].CompareTo(right[index]);
            if (order != 0) return order;
        }

        return 0;
    }
}
