using Backlog.UI.Components.Brain;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The two library parts of linking Brain notes and tasks (local ADR 0026 §3,
/// <c>.devbook/domain/brain/requirements.md#bi-directional-linking</c>): the task
/// chip in a note's Tasks section, and the teal "◆ n note" badge on a task row.
/// </summary>
public sealed class BrainTaskLinkComponentTests
{
    [Fact]
    public void A_known_task_is_a_gold_chip_that_opens_it()
    {
        using var context = new BunitContext();
        var opened = new List<string?>();

        var chip = context.Render<TaskLinkChip>(parameters => parameters
            .Add(p => p.Label, "Ship the badge")
            .Add(p => p.Reference, "6f9619ff-8b86-d011-b42d-00c04fc964ff")
            .Add(p => p.Resolved, true)
            .Add(p => p.OnOpen, (string? reference) => opened.Add(reference)));

        Assert.Contains("repo-link-chip--resolved", chip.Find(".task-link-chip").ClassList);
        chip.Find("button").Click();

        Assert.Equal(["6f9619ff-8b86-d011-b42d-00c04fc964ff"], opened);
    }

    [Fact]
    public void An_unknown_task_is_a_dashed_chip_saying_so_with_nothing_to_press()
    {
        using var context = new BunitContext();

        var chip = context.Render<TaskLinkChip>(parameters => parameters
            .Add(p => p.Label, "0c8a1b4e-2f6d-4a37-9c11-5e0b7d3f2a90")
            .Add(p => p.Reference, "0c8a1b4e-2f6d-4a37-9c11-5e0b7d3f2a90")
            .Add(p => p.OnOpen, (string? _) => { }));

        Assert.Contains("repo-link-chip--unresolved", chip.Find(".task-link-chip").ClassList);
        Assert.Equal("not in this backlog", chip.Find(".repo-link-chip__problem").TextContent.Trim());
        Assert.Empty(chip.FindAll("button"));
    }

    [Fact]
    public void A_badge_with_no_note_draws_nothing()
    {
        using var context = new BunitContext();

        var badge = context.Render<BrainNotesBadge>();

        Assert.Empty(badge.Markup.Trim());
    }

    [Fact]
    public void A_badge_with_several_notes_lists_them_and_opens_the_one_pressed()
    {
        using var context = new BunitContext();
        var opened = new List<BrainNoteLink>();
        var plan = new BrainNoteLink("projects/plan.md", "Plan");
        var standup = new BrainNoteLink("work/standup.md", "Standup");

        var badge = context.Render<BrainNotesBadge>(parameters => parameters
            .Add(p => p.Notes, [plan, standup])
            .Add(p => p.OnOpen, (BrainNoteLink note) => opened.Add(note)));

        var button = badge.Find("[data-testid='brain-notes-badge-button']");
        Assert.Contains("◆ 2 notes", button.TextContent, StringComparison.Ordinal);
        Assert.Equal("Linked from 2 of your Brain notes — only you see this", button.GetAttribute("aria-label"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Empty(badge.FindAll("[data-testid='brain-notes-badge-list']"));

        button.Click();
        var notes = badge.FindAll("[data-testid='brain-notes-badge-list'] button");
        Assert.Equal(["Plan", "Standup"], notes.Select(note => note.TextContent.Trim()));

        notes[1].Click();

        Assert.Equal([standup], opened);
        Assert.Empty(badge.FindAll("[data-testid='brain-notes-badge-list']"));
    }
}
