using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// Which notes a finished plan offers to archive, and how its Knowledge Refs follow a
/// moved note (local ADR 0052 §5).
/// </summary>
public sealed class FinishedProjectNoteTests
{
    private static RoadmapItemDto Item(string title) =>
        new(Guid.NewGuid(), title, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6), PlanningPriority.Medium, [], null, null, []);

    private static RoadmapGatheredLink Entry(RoadmapProgress progress) => new("t", "Task", 1, RollupOrigin.Tag, progress);

    private static RoadmapGatheredLink Note(string key, RoadmapUnresolvedReason? unresolved = null) =>
        new(key, "Note", null, RollupOrigin.Direct, UnresolvedBecause: unresolved);

    [Fact]
    public void Only_a_finished_plans_resolved_project_notes_are_selected()
    {
        var finished = Item("Finished");
        var open = Item("Open");
        var empty = Item("Gathers no task");
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>
        {
            [finished.Id] = new([Entry(RoadmapProgress.Done)], [], [
                Note("#brain:projects/b.md"),
                Note("#brain:projects/a.md#scope"),
                Note("#brain:projects/a.md"),
                Note("#brain:projects/gone.md", RoadmapUnresolvedReason.NotInBrain),
                Note("#brain:areas/health.md"),
            ]),
            [open.Id] = new([Entry(RoadmapProgress.Done), Entry(RoadmapProgress.InProgress)], [], [Note("#brain:projects/c.md")]),
            [empty.Id] = new([], [], [Note("#brain:projects/d.md")]),
        };

        var selected = FinishedProjectNote.Select(
            [finished, open, empty],
            rollups,
            path => path.StartsWith("projects/", StringComparison.Ordinal));

        Assert.Equal(["projects/a.md", "projects/b.md"], selected.Select(note => note.NotePath));
        Assert.All(selected, note => Assert.Equal(finished.Id, note.ItemId));
    }

    [Fact]
    public void Every_ref_naming_the_moved_note_follows_it_with_its_heading_kept()
    {
        var refs = FinishedProjectNote.Repoint(
            ["#brain:projects/a.md", "#BRAIN:projects/a.md#scope", "#brain:projects/b.md", "arc42/adr/0052.md"],
            "projects/a.md",
            "archive/a.md");

        Assert.Equal(["#brain:archive/a.md", "#brain:archive/a.md#scope", "#brain:projects/b.md", "arc42/adr/0052.md"], refs);
    }

    [Fact]
    public void Refs_that_never_named_the_note_are_left_alone()
    {
        Assert.Null(FinishedProjectNote.Repoint(["#brain:projects/b.md", "domain/x.md"], "projects/a.md", "archive/a.md"));
    }
}
