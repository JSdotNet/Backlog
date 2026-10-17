using Backlog.UI.Components.Goals;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The sample goals every page that draws a goal control picks from: the goal page, the
/// task side panel, the roadmap shelf and the work cards. One set, so a goal attached on
/// one page reads the same on the next.
/// </summary>
internal static class GoalFixtures
{
    public static readonly GoalOption Launch = new(Guid.Parse("7d2f6b9e-1c4a-4f0e-9a51-0b8c3e2d4f61"), "Launch the desktop app", 3, 8, new DateOnly(2026, 11, 14));

    public static readonly GoalOption Beta = new(Guid.Parse("2a8e4c1d-5b7f-4e93-8d06-c4f1a9b2e375"), "Docs for the beta", 1, 5, new DateOnly(2026, 11, 28));

    public static readonly GoalOption Sync = new(Guid.Parse("c95b0e3a-8f21-4d7c-b6e4-3a7d1f0c9e82"), "Sync between devices", 0, 0, new DateOnly(2026, 12, 19));

    /// <summary>The open goals, by due date — the order a host offers them in.</summary>
    public static readonly IReadOnlyList<GoalOption> Open = [Launch, Beta, Sync];
}
