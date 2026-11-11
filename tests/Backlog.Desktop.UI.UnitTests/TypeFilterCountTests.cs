using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The number on a type chip is how many entries of that type are still to do, the
/// way a tag chip's is: a ticked-off entry is still listed while no status is
/// pressed, so the tooltip carries it, never the face.
/// </summary>
public sealed class TypeFilterCountTests
{
    private const string Chip = "[data-testid='type-filter-option']";

    [Fact]
    public async Task A_type_chip_counts_its_open_entries_and_its_tooltip_the_completed_ones()
    {
        using var host = await TasksPaneHost.CreateAsync();

        await host.WriteEntryAsync("# Draft the brief\n`prompt` `!ready`\n");
        await host.WriteEntryAsync("# Review the brief\n`prompt` `!ready`\n");
        await host.WriteEntryAsync("# Send the brief\n`prompt` `!done` `completed:2026-09-22`\n");
        await host.WriteEntryAsync("# Water the plants\n`task` `!ready`\n");
        await host.WriteEntryAsync("# Prove the import\n`test` `!done` `completed:2026-09-23`\n");
        await host.State.SelectAsync(null);

        Assert.Equal(
            [(EntryType.Prompt, 2, 1), (EntryType.Task, 1, 0), (EntryType.Test, 0, 1)],
            host.State.TypeFilters.Select(option => (option.Value, option.OpenCount, option.CompletedCount)));

        var chips = host.Render().FindAll(Chip);

        Assert.Equal(["Prompt2", "Task1", "Test0"], chips.Select(chip => chip.TextContent.Trim()));
        Assert.Equal(
            ["Show only prompts — 2 open, 1 completed", "Show only tasks — 1 open", "Show only tests — 0 open, 1 completed"],
            chips.Select(chip => chip.GetAttribute("title")));
    }
}
