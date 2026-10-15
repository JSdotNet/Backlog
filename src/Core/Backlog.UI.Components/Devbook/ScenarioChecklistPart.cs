namespace Backlog.UI.Components.Devbook;

/// <summary>One row of a <see cref="ScenarioPartChecklist"/>.</summary>
/// <param name="Title">The part's heading.</param>
/// <param name="State"><c>passed</c>, <c>failed</c>, <c>stale</c> or
/// <c>never-run</c> — a run's <c>not-run</c> reads as never run.</param>
/// <param name="Stem">The scenario page's stem — its file name without <c>.md</c>,
/// which is also its run folder's name — or null. The stem rather than the page's
/// title, because it is what a run, a spec and a screenshot path are all named by,
/// so the row reads the same as the evidence it summarises.</param>
/// <param name="LastRun">When the part last ran, already formatted, or null for
/// never.</param>
/// <param name="Reference">The part as <c>&lt;page&gt;.md#&lt;part&gt;</c>, for its
/// tooltip and its opener, or null.</param>
/// <param name="FailedStep">The 1-based step the last run stopped on, for a failed
/// part whose run recorded one; the row ignores it for any other state.</param>
public sealed record ScenarioChecklistPart(
    string Title,
    string State,
    string? Stem,
    string? LastRun,
    string? Reference,
    int? FailedStep = null)
{
    internal string Key => Reference ?? $"{Stem}#{Title}";
}
