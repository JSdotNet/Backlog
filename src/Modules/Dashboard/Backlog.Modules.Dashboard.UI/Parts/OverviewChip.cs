using Backlog.UI.Components.Metrics;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// The tone an Overview chip sits on. The words carry the meaning; the tone only draws
/// the eye to the chip that needs it, the way every other state chip in the product does.
/// </summary>
public enum OverviewTone
{
    /// <summary>Nothing to say either way: no comparison, or no change.</summary>
    Calm,

    /// <summary>The figure moved the good way, or nothing is wrong.</summary>
    Good,

    /// <summary>The figure moved the bad way, or something is heading wrong — behind
    /// pace, passing a budget.</summary>
    Warning,

    /// <summary>Drift: the devbook and its code disagree.</summary>
    Drift
}

/// <summary>What an Overview chip says, and the tone it says it on.</summary>
/// <param name="Text">The chip's words — the whole of its meaning.</param>
/// <param name="Tone">The surface it sits on.</param>
public sealed record OverviewChip(string Text, OverviewTone Tone)
{
    /// <summary>
    /// A previous-window delta as a chip: its words as a metric tile writes them —
    /// "up 4 vs previous 4 weeks" — on the good tone when it moved the way the caller
    /// said is better, the warning tone when it moved the other way, and calm when it
    /// did not move. Null in, null out: no earlier figure draws no chip.
    /// </summary>
    public static OverviewChip? FromDelta(MetricDelta? delta)
    {
        if (delta is null) return null;

        var change = MetricFormat.Delta(delta);
        var text = string.IsNullOrWhiteSpace(delta.ComparedTo) ? change : $"{change} vs {delta.ComparedTo}";

        var tone = delta.Value == 0m
            ? OverviewTone.Calm
            : delta.Value > 0m == delta.HigherIsBetter ? OverviewTone.Good : OverviewTone.Warning;

        return new OverviewChip(text, tone);
    }

    /// <summary>The class modifier the tone draws with.</summary>
    public string Modifier => Tone switch
    {
        OverviewTone.Good => "good",
        OverviewTone.Warning => "warning",
        OverviewTone.Drift => "drift",
        _ => "calm"
    };
}
