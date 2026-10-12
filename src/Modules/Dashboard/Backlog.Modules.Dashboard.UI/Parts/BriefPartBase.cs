using Backlog.UI.Components.Metrics;
using Microsoft.AspNetCore.Components;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// What the weekly brief's four blocks share: the week they report, the link to their
/// tab, and how a reading of that week becomes a block's state.
/// </summary>
/// <remarks>
/// <para>
/// A dashboard part like any other — it fetches through its tab's insight, so it shares
/// that tab's cached read, and carries its own status — with one difference: the week.
/// Moving to the week before is a derivation over what was already read, so it moves
/// what the block draws and never what it fetches.
/// </para>
/// <para>
/// A source that answered but does not reach back to the reported week is as missing as
/// one that refused, and says so in the same one line.
/// </para>
/// </remarks>
/// <typeparam name="T">What the block's insight answers.</typeparam>
public abstract class BriefPartBase<T> : DashboardPartBase<T>
    where T : class
{
    /// <summary>What a block says when its source answered with weeks that stop short
    /// of the reported one.</summary>
    public const string OutOfReach = "The figures read do not reach back to this week.";

    /// <summary>The week the brief reports.</summary>
    [Parameter, EditorRequired]
    public BriefWeek Week { get; set; } = default!;

    /// <summary>Raised with the pane's id of the tab the block links to. With nobody
    /// listening the block draws no link.</summary>
    [Parameter]
    public EventCallback<string> OnOpenTab { get; set; }

    /// <summary>The block's state for <paramref name="reading"/>: the part's own while it
    /// has no answer, unavailable when the answer does not reach the week.</summary>
    protected MetricStatusKind BlockStatus(BriefReading? reading) =>
        IsReady
            ? reading is null ? MetricStatusKind.Unavailable : MetricStatusKind.Ready
            : Status is MetricStatusKind.Loading ? MetricStatusKind.Loading : MetricStatusKind.Unavailable;

    /// <summary>Why the block has no figures, beside <see cref="BlockStatus"/>.</summary>
    protected string? BlockMessage(BriefReading? reading) =>
        IsReady && reading is null ? OutOfReach : StatusMessage;

    /// <summary>The reading's bars as the chart takes them.</summary>
    protected static IReadOnlyList<MetricPoint> Points(BriefReading? reading) =>
        reading is null ? [] : [.. reading.Bars.Select(point => new MetricPoint(point.Label, point.Value))];

    /// <summary>The chart's caption: what the bars are and the weeks they run over.</summary>
    protected static string Caption(string what, BriefReading? reading) =>
        reading is { Bars.Count: > 1 } read
            ? $"{what}, {read.Bars[0].Label}–{read.Bars[^1].Label}"
            : what;

    /// <summary>Opens the block's tab.</summary>
    protected EventCallback Open(string tabId) =>
        OnOpenTab.HasDelegate
            ? EventCallback.Factory.Create(this, () => OnOpenTab.InvokeAsync(tabId))
            : default;

    /// <summary>A brief block is never empty: a week with nothing in it is a zero, and
    /// the sentence says so.</summary>
    protected override bool IsEmpty(T value) => false;
}
