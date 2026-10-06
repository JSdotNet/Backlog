namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// How this device last had the roadmap drawn: choices about the view, not the plan.
/// <para>
/// Kept per device and never synced (local ADR 0019, §4): two devices may draw the same
/// plan differently, and neither choice moves a bar. A port on Roadmap's own surface,
/// answered by the host over its per-device settings, because a module may not reference
/// infrastructure (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>).
/// </para>
/// </summary>
public interface IRoadmapViewPreferences
{
    /// <summary>Whether the day and week heads show their hours line — the "Hours"
    /// switch on the roadmap's toolbar. On until the reader turns it off on this device.
    /// While it is off the roadmap does not read the actual hours either.</summary>
    bool HoursShown { get; }

    /// <summary>Remembers the Hours switch on this device. A choice the host cannot keep
    /// for next time still holds for this run.</summary>
    void SetHoursShown(bool shown);

    /// <summary>The bands, by group id, the reader folded to one lane on this device —
    /// drawn folded when the roadmap opens again. Empty until one is folded.</summary>
    IReadOnlyCollection<string> CollapsedGroups { get; }

    /// <summary>Remembers which bands are folded on this device, replacing what was kept.
    /// A choice the host cannot keep for next time still holds for this run.</summary>
    void SetCollapsedGroups(IReadOnlyCollection<string> collapsed);
}
