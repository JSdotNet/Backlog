using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.UI;
using Backlog.UI.Components.Brain;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A roadmap item's Brain notes in its editor (local ADR 0053): a group of their own,
/// apart from the repository's chapters, each a Brain chip. A resolved chip opens the
/// note, raised as its path inside the Brain; an unresolved one shows its address,
/// opens nothing, and says why in words.
/// </summary>
public sealed class RoadmapItemBrainNotesSurfaceTests : IDisposable
{
    private readonly BunitContext _context = new();

    public RoadmapItemBrainNotesSurfaceTests()
    {
        // The modal reaches for JS to trap focus; nothing asserted here depends on it.
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static RoadmapItemDto Item() =>
        new(
            Guid.NewGuid(),
            "Ship the thing",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            PlanningPriority.Medium,
            [],
            Lane: null,
            TaskId: null,
            DependsOn: [],
            Tag: "sync");

    private static readonly RoadmapItemRollupDto Gathered = new(
        [],
        [new RoadmapGatheredLink(".devbook/domain/tasks/domain.md#task", "Task", 3, RollupOrigin.Direct)],
        [
            new RoadmapGatheredLink("#brain:projects/sync.md#later", "Later", 5, RollupOrigin.Both),
            new RoadmapGatheredLink("#brain:projects/gone.md", "#brain:projects/gone.md", null, RollupOrigin.Direct, UnresolvedBecause: RoadmapUnresolvedReason.NotInBrain)
        ]);

    private static RoadmapItemRollupDto OneUnresolved(string key, RoadmapUnresolvedReason reason) =>
        new([], [], [new RoadmapGatheredLink(key, key, null, RollupOrigin.Direct, UnresolvedBecause: reason)]);

    private IRenderedComponent<RoadmapItemEditor> Render(List<string> opened) =>
        _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(editor => editor.Open, true)
            .Add(editor => editor.Item, Item())
            .Add(editor => editor.Rollup, Gathered)
            .Add(editor => editor.OnOpenBrainNote, (string note) => opened.Add(note)));

    [Fact]
    public void Brain_notes_are_their_own_group_one_brain_chip_each()
    {
        var editor = Render([]);

        var group = editor.Find("[data-testid='roadmap-item-linked-brain']");
        Assert.Equal(2, group.QuerySelectorAll(".brain-note-chip").Length);
        Assert.All(group.QuerySelectorAll(".brain-note-chip"), chip => Assert.Contains("Brain", chip.QuerySelector(".badge--brain")!.TextContent, StringComparison.Ordinal));
        Assert.Contains("5 points", group.TextContent, StringComparison.Ordinal);

        // The repository's chapter stays where it was, and wears no Brain chip.
        var knowledge = editor.Find("[data-testid='roadmap-item-linked-knowledge']");
        Assert.Contains("Task", knowledge.TextContent, StringComparison.Ordinal);
        Assert.Empty(knowledge.QuerySelectorAll(".brain-note-chip"));
    }

    [Fact]
    public void Pressing_a_resolved_chip_raises_the_note_s_path_inside_the_brain()
    {
        var opened = new List<string>();
        var editor = Render(opened);

        var resolved = editor.Find("[data-testid='roadmap-item-brain-note'][data-resolved='true']");
        resolved.QuerySelector("button")!.Click();

        Assert.Equal(["projects/sync.md#later"], opened);
    }

    [Fact]
    public void An_unresolved_chip_shows_its_address_opens_nothing_and_says_why()
    {
        var editor = Render([]);

        var unresolved = editor.Find("[data-testid='roadmap-item-brain-note'][data-resolved='false']");
        Assert.Null(unresolved.QuerySelector("button"));
        Assert.Contains("#brain:projects/gone.md", unresolved.TextContent, StringComparison.Ordinal);
        Assert.Contains("brain-note-chip--unresolved", unresolved.ClassName, StringComparison.Ordinal);

        // The owner's Brain was read and the note has gone: it says so, and never that
        // only the owner can open it.
        var mark = unresolved.QuerySelector(".brain-note-chip__unresolved")!;
        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.Equal("Not in your Brain: it holds no note at projects/gone.md.", mark.GetAttribute("aria-label"));
        Assert.Contains("it holds no note at projects/gone.md", unresolved.QuerySelector(".brain-note-chip__label")!.GetAttribute("title"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#brain:projects/sync.md", RoadmapUnresolvedReason.NoBrain, BrainNoteChip.NotInYourBrain)]
    [InlineData("#brain:projects/gone.md#later", RoadmapUnresolvedReason.NotInBrain, "Not in your Brain: it holds no note at projects/gone.md#later.")]
    [InlineData("#brain:../outside.md", RoadmapUnresolvedReason.NotAnAddress, BrainNoteChip.NotABrainAddress)]
    public void Each_reason_a_note_is_unresolved_is_said_in_its_own_words(string key, RoadmapUnresolvedReason reason, string expected)
    {
        var editor = _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(editor => editor.Open, true)
            .Add(editor => editor.Item, Item())
            .Add(editor => editor.Rollup, OneUnresolved(key, reason)));

        var mark = editor.Find("[data-testid='roadmap-item-brain-note'] .brain-note-chip__unresolved");
        Assert.Equal(expected, mark.GetAttribute("aria-label"));
    }

    /// <summary>Pressing a chip closes the dialog, so while a field holds an unsaved
    /// change the chip waits and says why, rather than discarding the edit.</summary>
    [Fact]
    public void After_a_field_edit_a_resolved_chip_is_disabled_and_its_name_says_why()
    {
        var editor = Render([]);

        editor.Find("[data-testid=\"roadmap-editor-title\"] input").Input("Ship the thing, renamed");

        editor.WaitForAssertion(() =>
        {
            var button = editor.Find("[data-testid='roadmap-item-brain-note'][data-resolved='true'] button");
            Assert.True(button.HasAttribute("disabled"));
            Assert.Contains("Save or cancel your changes first", button.GetAttribute("aria-label"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void With_no_host_to_open_a_note_every_chip_is_text()
    {
        var editor = _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(editor => editor.Open, true)
            .Add(editor => editor.Item, Item())
            .Add(editor => editor.Rollup, Gathered));

        Assert.Empty(editor.Find("[data-testid='roadmap-item-linked-brain']").QuerySelectorAll("button"));
    }

    public void Dispose() => _context.Dispose();
}
