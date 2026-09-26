namespace Backlog.UI.Components.Roadmap;

/// <summary>
/// One piece of the work a bar stands for, drawn inside the bar's window when the
/// bar is expanded.
/// <para>
/// A step has an effort and no dates. Where it is drawn is a <em>proportion</em> of
/// its bar — its effort's share of the whole — and not a schedule: a step drawn
/// across the 14th is not planned for the 14th. The timeline says so wherever a
/// reader could take it otherwise.
/// </para>
/// <para>
/// The order is the caller's. The timeline draws steps in the order it is handed,
/// one row each, because only the caller knows what waits on what.
/// </para>
/// </summary>
/// <param name="Id">Unique within its bar.</param>
/// <param name="Title">What the step is.</param>
/// <param name="Effort">What it registered as effort, or <see langword="null"/>
/// when it registered none. An unestimated step is drawn at
/// <see cref="RoadmapStepLayout.MinStepWidthRem"/> and marked as unsized; <c>0</c>
/// is a real estimate and is drawn as one.</param>
/// <param name="Tone">Which status colour it wears. See
/// <see cref="RoadmapStepTone"/>.</param>
/// <param name="Status">The status in the caller's own words — read out and shown
/// on the tooltip, because the colour is never the only thing saying it.</param>
/// <param name="Detail">Anything else worth reading out, such as what it waits
/// for.</param>
public sealed record RoadmapStep(
    string Id,
    string Title,
    int? Effort,
    RoadmapStepTone Tone = RoadmapStepTone.Unknown,
    string? Status = null,
    string? Detail = null)
{
    public bool IsEstimated => Effort is not null;

    public bool IsDone => Tone == RoadmapStepTone.Done;
}

/// <summary>
/// The status colours a step can wear: the four the status badge already paints,
/// and none.
/// <para>
/// Named after the badge classes rather than after any one vocabulary, because the
/// vocabulary is the caller's and the colour is the stylesheet's
/// (<c>.devbook/design/color-scheme.md#badge-and-chip-tones</c>). A caller maps its own
/// words onto these; a sixth colour would be a second palette.
/// </para>
/// </summary>
public enum RoadmapStepTone
{
    /// <summary>Nothing registered. Outlined, spending no colour.</summary>
    Unknown,

    /// <summary>Not yet picked up — the draft badge's surface.</summary>
    Draft,

    /// <summary>Ready to start — the ready badge's surface.</summary>
    Ready,

    /// <summary>Being worked on — the in-progress badge's surface.</summary>
    InProgress,

    /// <summary>Finished — the done badge's surface.</summary>
    Done
}

/// <summary>Where one step is drawn, relative to the left edge of its bar.</summary>
/// <param name="Offset">How far in from the bar's left edge it starts, in rem.</param>
/// <param name="Width">How wide it is drawn, in rem.</param>
/// <param name="Share">Its effort's share of the bar's estimated effort, 0 to 1.
/// Zero for an unestimated step, which has no share to speak of.</param>
public sealed record RoadmapStepSpan(double Offset, double Width, double Share);

/// <summary>
/// The arithmetic that turns steps into widths.
/// <para>
/// Each unestimated step takes <see cref="MinStepWidthRem"/>; the estimated steps
/// share what is left of the bar's width by their effort. Laid end to end in the
/// order given, so a chain reads left to right as a staircase.
/// </para>
/// <para>
/// A pure function, separate from the markup, so the widths can be asserted as
/// numbers rather than read back out of a style attribute.
/// </para>
/// </summary>
public static class RoadmapStepLayout
{
    /// <summary>
    /// How wide an unestimated step is drawn.
    /// <para>
    /// Wide enough to carry its marker, and fixed rather than guessed: an unsized
    /// step has no share to scale by, and inventing one would draw an estimate
    /// nobody made. When the unsized steps alone are wider than the bar, they still
    /// take this each and the estimated steps get nothing — the staircase then runs
    /// past the bar's end, which is the honest picture of a plan whose size is
    /// mostly unknown.
    /// </para>
    /// </summary>
    public const double MinStepWidthRem = 1;

    /// <summary>How narrow an estimated step may get, so a step estimated at zero
    /// is a visible sliver rather than nothing at all.</summary>
    public const double HairlineRem = 0.25;

    public static IReadOnlyList<RoadmapStepSpan> Lay(IReadOnlyList<RoadmapStep> steps, double barWidthRem)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var unsized = steps.Count(step => !step.IsEstimated);
        var estimated = steps.Where(step => step.IsEstimated).Sum(step => Math.Max(0, step.Effort!.Value));
        var remaining = Math.Max(0, barWidthRem - unsized * MinStepWidthRem);

        var spans = new List<RoadmapStepSpan>(steps.Count);
        var offset = 0d;

        foreach (var step in steps)
        {
            double width;
            double share;

            if (!step.IsEstimated)
            {
                width = MinStepWidthRem;
                share = 0;
            }
            else
            {
                // All estimated at zero: nothing to divide by, and nothing to divide.
                share = estimated == 0 ? 0 : (double)Math.Max(0, step.Effort!.Value) / estimated;
                width = Math.Max(HairlineRem, remaining * share);
            }

            spans.Add(new RoadmapStepSpan(offset, width, share));
            offset += width;
        }

        return spans;
    }
}
