using System.Globalization;

using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// What the task, session and annotation exchanges share: the pull URL, the
/// cursor answers a device recovers from, and how far a watermark may move
/// after a batch. They differ only by route and by which stamp a record
/// carries, so those are the parameters.
/// </summary>
internal static class ReplicaRoutes
{
    /// <summary>The pull URL for <paramref name="route"/> with its query. The
    /// cursor is escaped rather than concatenated: it is base64 of a signed
    /// payload, so <c>+</c> and <c>=</c> are ordinary characters in it and a raw
    /// <c>+</c> would arrive as a space and fail its own signature check.</summary>
    internal static string PullRoute(string route, string? since, int maxItems)
    {
        var pull = $"{SyncRoutes.Absolute(route)}?maxItems={maxItems.ToString(CultureInfo.InvariantCulture)}";

        return string.IsNullOrWhiteSpace(since)
            ? pull
            : $"{pull}&since={Uri.EscapeDataString(since)}";
    }

    /// <summary>The two answers that mean "that cursor is no longer one you can
    /// resume from", which the device recovers from by forgetting it. Neither
    /// says anything about the owner's records, so starting over loses nothing
    /// but the position — and a record that arrives twice lands on the row it
    /// already wrote.</summary>
    internal static bool Retired(string code) =>
        code is SyncErrorCodes.SyncCursorExpired or SyncErrorCodes.SyncCursorMalformed;

    /// <summary>
    /// How far the watermark may move once a batch has been accepted, or null
    /// when it may not move at all.
    /// <para>
    /// The last stamp in the batch on the final batch, because everything that
    /// was selected has then been sent — including a whole backlog imported from
    /// one clock reading, which would otherwise be re-pushed on every sync for
    /// ever. Anywhere else it is the highest stamp strictly below the batch's
    /// last, because a record sharing that last stamp may still be waiting in the
    /// next batch and the selection would never offer it again.
    /// </para>
    /// <para>
    /// Null when a whole batch shares one stamp: there is nowhere safe to move
    /// to, so the batch is simply sent again next run. Free under a
    /// whole-document upsert, and the alternative is losing the records that
    /// share it.
    /// </para>
    /// </summary>
    internal static DateTimeOffset? WatermarkAfter<T>(IReadOnlyList<T> batch, bool final, Func<T, DateTimeOffset> stamp)
    {
        if (final) return stamp(batch[^1]);

        var boundary = stamp(batch[^1]);

        return batch
            .Select(stamp)
            .Where(value => value < boundary)
            .Select(value => (DateTimeOffset?)value)
            .Max();
    }
}
