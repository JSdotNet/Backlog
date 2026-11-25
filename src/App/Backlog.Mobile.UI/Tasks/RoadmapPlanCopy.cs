using System.Globalization;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The roadmap plan document as the phone last pulled it: the whole plan a desktop writes
/// on the task feed (.devbook/arc42/adr/0018), kept verbatim beside the rows. The phone
/// reads one thing off it — which plans were put on My Day, and for which day (local ADR
/// 0062 §8) — and never writes it.
/// </summary>
/// <param name="UpdatedAt">The document's own last-write-wins stamp.</param>
/// <param name="ServerTimestamp">The replica's ordering stamp, which breaks a tie between
/// two copies with the same <paramref name="UpdatedAt"/>, as it does for a task row.</param>
/// <param name="Content">The document's JSON, verbatim, unknown keys included.</param>
public sealed record RoadmapPlanCopy(DateTimeOffset UpdatedAt, long ServerTimestamp, string Content)
{
    // Persisted and synced on the task feed with the whole document; spelled as the
    // desktop's RoadmapItemDocument writes them, and never to be renamed.
    private const string ItemsKey = "items";
    private const string TagKey = "tag";
    private const string MyDayOnKey = "myDayOn";

    /// <summary>
    /// The plans put on My Day and the day each was picked for, in the order the plan lists
    /// its items, keyed by <see cref="TodayPlanLine.PlanKey"/>. A shared tag is one plan; its
    /// items take the date together, so they agree unless a device wrote one alone, and then
    /// the latest day is read — as the desktop reads it.
    /// </summary>
    public IReadOnlyList<(string Plan, DateOnly On)> PlansOnMyDay => Read(Content);

    /// <summary>Whether <paramref name="content"/> is a plan document this build can read:
    /// a JSON object. An item it cannot read is skipped, not the document.</summary>
    public static bool IsReadable(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<(string Plan, DateOnly On)> Read(string content)
    {
        var plans = new List<(string Plan, DateOnly On)>();
        if (!IsReadable(content)) return plans;

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty(ItemsKey, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return plans;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty(TagKey, out var tag) || tag.ValueKind != JsonValueKind.String) continue;
            if (!item.TryGetProperty(MyDayOnKey, out var on) || on.ValueKind != JsonValueKind.String) continue;
            if (!DateOnly.TryParseExact(on.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;

            var plan = TodayPlanLine.PlanKey(tag.GetString());
            if (plan.Length == 0) continue;

            var seen = plans.FindIndex(entry => string.Equals(entry.Plan, plan, StringComparison.Ordinal));
            if (seen < 0) plans.Add((plan, day));
            else if (day > plans[seen].On) plans[seen] = (plan, day);
        }

        return plans;
    }

    /// <summary>
    /// The plan document among <paramref name="records"/> that replaces
    /// <paramref name="current"/>, or null when none does — ordered as
    /// <see cref="MyDayPreferencesDocument.Fold"/> orders its copies, so the order pages
    /// arrive in does not change which copy the phone ends on. A copy this build cannot
    /// read is passed over; the desktop never writes a tombstone for the plan, so none is
    /// taken.
    /// </summary>
    public static RoadmapPlanCopy? Fold(RoadmapPlanCopy? current, IEnumerable<TaskChangeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var best = current;

        foreach (var record in records)
        {
            var change = record.Change;
            if (!string.Equals(change.Task.Type, TaskFold.RoadmapPlanType, StringComparison.Ordinal)) continue;
            if (change.DeletedAt is not null || !IsReadable(change.Task.ContentMd)) continue;

            var incoming = new RoadmapPlanCopy(change.UpdatedAt, record.ServerTimestamp, change.Task.ContentMd);
            if (best is null || Wins(incoming, best)) best = incoming;
        }

        return ReferenceEquals(best, current) ? null : best;
    }

    private static bool Wins(RoadmapPlanCopy incoming, RoadmapPlanCopy current) =>
        incoming.UpdatedAt != current.UpdatedAt
            ? incoming.UpdatedAt > current.UpdatedAt
            : incoming.ServerTimestamp > current.ServerTimestamp;
}
