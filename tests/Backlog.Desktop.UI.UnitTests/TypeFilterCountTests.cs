using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The number on a type chip is how many entries of that type are still to do, the
/// way a tag chip's is: a ticked-off entry is still listed while no status is
/// pressed, so the tooltip carries it, never the face. A type with nothing left to
/// do has no chip at all, unless it is the one pressed.
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
            [(EntryType.Prompt, 2, 1), (EntryType.Task, 1, 0)],
            host.State.TypeFilters.Select(option => (option.Value, option.OpenCount, option.CompletedCount)));

        var chips = host.Render().FindAll(Chip);

        Assert.Equal(["Prompt2", "Task1"], chips.Select(chip => chip.TextContent.Trim()));
        Assert.Equal(
            ["Show only prompts — 2 open, 1 completed", "Show only tasks — 1 open"],
            chips.Select(chip => chip.GetAttribute("title")));
    }

    /// <summary>The chip under the reader's press is what says the list is narrowed,
    /// so ticking off the last open entry of that type keeps it, pressed, at 0.</summary>
    [Fact]
    public async Task A_pressed_type_keeps_its_chip_and_its_press_when_its_last_open_entry_is_ticked_off()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var prompt = await host.WriteEntryAsync("# Draft the brief\n`prompt` `!ready`\n");
        await host.WriteEntryAsync("# Water the plants\n`task` `!ready`\n");
        await host.State.SelectAsync(null);
        host.State.SetTypeFilter(EntryType.Prompt);

        await host.State.ToggleCompletedAsync(host.State.Rows.Single(row => row.Id == prompt.Id), new DateOnly(2026, 11, 11));

        Assert.Equal(EntryType.Prompt, host.State.SelectedType);
        Assert.Contains((EntryType.Prompt, 0, 1), host.State.TypeFilters.Select(option => (option.Value, option.OpenCount, option.CompletedCount)));
        Assert.Equal("true", host.Render().Find($"{Chip}[data-type='prompt']").GetAttribute("aria-pressed"));
    }

    /// <summary>One chip narrows nothing, and a finished type does not count towards
    /// the two the group needs.</summary>
    [Fact]
    public async Task A_type_group_left_with_one_chip_once_finished_types_drop_is_not_drawn()
    {
        using var host = await TasksPaneHost.CreateAsync();

        await host.WriteEntryAsync("# Draft the brief\n`prompt` `!ready`\n");
        await host.WriteEntryAsync("# Prove the import\n`test` `!done` `completed:2026-09-23`\n");
        await host.State.SelectAsync(null);

        Assert.Empty(host.State.TypeFilters);
        Assert.Empty(host.Render().FindAll(".filter-group--type"));
    }
}
