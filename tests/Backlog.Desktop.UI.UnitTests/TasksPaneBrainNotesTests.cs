using Backlog.UI.Components.Brain;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A task row's Brain badge (local ADR 0026 §3,
/// <c>.devbook/domain/devbook/requirements.md#bi-directional-linking</c>): the teal
/// "◆ n note" the host draws for the owner's Brain notes that name the task, which
/// opens them. The pane asks the host by the row's stored id and draws nothing the
/// host does not answer — the Brain is personal, so another reader sees no badge.
/// </summary>
public sealed class TasksPaneBrainNotesTests
{
    private const string Linked = "# Ship the badge\n`task` `repo:backlog`\n\nDraw it.\n";
    private const string Plain = "# Nothing linked\n`task`\n\nPlain.\n";

    [Fact]
    public async Task A_row_named_by_brain_notes_carries_a_teal_badge_counting_them()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var linked = await host.WriteEntryAsync(Linked);
        await host.WriteEntryAsync(Plain);
        var asked = new List<string>();

        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.BrainNotesFor, id =>
            {
                asked.Add(id);
                return id == linked.Id!.Value.ToString("D")
                    ? [new BrainNoteLink("projects/plan.md", "Plan"), new BrainNoteLink("work/standup.md", "Standup")]
                    : [];
            }));

        var badge = Assert.Single(pane.FindAll("[data-testid='entry-list'] [data-testid='entry-brain-notes']"));
        Assert.Contains("◆ 2 notes", badge.TextContent, StringComparison.Ordinal);
        Assert.Contains("only you see this", badge.QuerySelector("button")!.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Contains(linked.Id!.Value.ToString("D"), asked);
    }

    [Fact]
    public async Task With_no_brain_wired_no_row_carries_a_badge()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(Linked);

        var pane = host.Render();

        Assert.NotNull(pane.Find("[data-testid='entry-list']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-brain-notes']"));
    }

    [Fact]
    public async Task Pressing_a_one_note_badge_opens_that_note()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync(Linked);
        var opened = new List<BrainNoteLink>();
        var note = new BrainNoteLink("projects/plan.md", "Plan");

        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.BrainNotesFor, _ => [note])
            .Add(p => p.OnOpenBrainNote, (BrainNoteLink link) => opened.Add(link)));

        var button = pane.Find("[data-testid='entry-list'] [data-testid='entry-brain-notes'] button");
        Assert.Contains("◆ 1 note", button.TextContent, StringComparison.Ordinal);
        button.Click();

        pane.WaitForAssertion(() => Assert.Equal([note], opened));
    }
}
