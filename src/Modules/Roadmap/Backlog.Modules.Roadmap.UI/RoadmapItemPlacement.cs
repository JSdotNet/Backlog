namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Where new work was asked for on the chart: the weeks, and the band and lane of the
/// row a double-click or a drag landed on. The editor opens on it instead of its own
/// defaults, so work added in place is filed where it was put — and every field still
/// says so before anything is saved.
/// </summary>
/// <param name="Start">The first day of the week under the pointer.</param>
/// <param name="Repository">The band's repository alias, or null for the unfiled
/// band, which files the work nowhere in particular.</param>
/// <param name="Lane">The row's lane, or null for the lane new work takes anyway.</param>
/// <param name="End">The last day a drag across the row covered, or null for a
/// double-click, which leaves the editor's own default length.</param>
public sealed record RoadmapItemPlacement(DateOnly Start, string? Repository, string? Lane, DateOnly? End = null);
