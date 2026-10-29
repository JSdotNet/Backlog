using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The Backlog entry a run's prompt names (<c>.devbook/domain/sessions/domain.md#delivery-run-reference</c>).
/// Two markers can, both the product's own: the line the app puts on every entry it copies,
/// which names the entry by its stored id, and the import plan's plan item marker, which names
/// it by plan and item. One reading for every caller — the reader's task reference and the
/// Personal Validation question's follow-up — so the two never disagree about which entry a
/// run works.
/// </summary>
internal static partial class RunEntryMarkers
{
    /// <summary>The entry a prompt names, or null where it names none.</summary>
    /// <param name="EntryId">The entry's id, from the entry marker, or null.</param>
    /// <param name="Plan">The bare plan tag, from the plan item marker, or null.</param>
    /// <param name="Item">The item id inside that plan, or null.</param>
    /// <param name="EntryMarkerEnd">Where the entry marker ends in the prompt, or -1: the
    /// copied entry's title is written on the line under it.</param>
    internal sealed record Named(Guid? EntryId, string? Plan, string? Item, int EntryMarkerEnd);

    internal static Named? In(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        var entry = EntryMarker().Match(prompt);
        var plan = PlanMarker().Match(prompt);

        if (!entry.Success && !plan.Success) return null;

        return new Named(
            entry.Success ? Guid.Parse(entry.Groups[1].Value) : null,
            plan.Success ? plan.Groups[2].Value : null,
            plan.Success ? plan.Groups[1].Value : null,
            entry.Success ? entry.Index + entry.Length : -1);
    }

    [GeneratedRegex("""Backlog plan item [`'"]([\w.-]+)[`'"] of plan [`'"]([\w.-]+)[`'"]""")]
    private static partial Regex PlanMarker();

    // The skill was backlog-run-plan-item before the plugin dropped its prefix; stored runs keep that spelling.
    [GeneratedRegex("""(?:backlog-)?run-plan-item entry `([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})`""")]
    private static partial Regex EntryMarker();
}
