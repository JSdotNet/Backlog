namespace Backlog.UI.Components.Devbook;

/// <summary>One row of a <see cref="ScenarioPartChecklist"/>.</summary>
/// <param name="Title">The part's heading.</param>
/// <param name="State"><c>passed</c>, <c>failed</c>, <c>stale</c> or
/// <c>never-run</c> — a run's <c>not-run</c> reads as never run.</param>
/// <param name="Page">The scenario page the part belongs to, as a reader names it,
/// or null.</param>
/// <param name="LastRun">When the part last ran, already formatted, or null for
/// never.</param>
/// <param name="Reference">The part as <c>&lt;page&gt;.md#&lt;part&gt;</c>, for its
/// tooltip and its opener, or null.</param>
public sealed record ScenarioChecklistPart(string Title, string State, string? Page, string? LastRun, string? Reference)
{
    internal string Key => Reference ?? $"{Page}#{Title}";
}
