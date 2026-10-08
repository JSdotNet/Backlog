using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The agenda time — <c>at:HH:MM</c> and <c>for:&lt;minutes&gt;m</c> beside
/// <c>myday:</c> — as .devbook/design/content-editing.md and
/// .devbook/domain/tasks/domain.md#agenda-time specify it: read and written on the
/// metadata line, refused when <c>for:</c> stands alone, 30 minutes when
/// <c>at:</c> does, and dropped whenever the My Day date changes or goes.
/// </summary>
public sealed class AgendaTimeTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    // --- The value object -------------------------------------------------

    [Fact]
    public void A_block_runs_from_its_start_for_its_duration()
    {
        var agenda = new AgendaTime(new TimeOnly(10, 45), 45);

        Assert.Equal(new TimeOnly(11, 30), agenda.End);
        Assert.Equal("10:45", agenda.StartToken);
    }

    [Fact]
    public void A_start_without_a_duration_means_thirty_minutes()
    {
        Assert.Equal(30, new AgendaTime(new TimeOnly(9, 0)).DurationMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public void A_duration_must_be_positive(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgendaTime(new TimeOnly(9, 0), minutes));
    }

    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal(new AgendaTime(new TimeOnly(10, 45), 45), new AgendaTime(new TimeOnly(10, 45, 30), 45));
        Assert.NotEqual(new AgendaTime(new TimeOnly(10, 45), 45), new AgendaTime(new TimeOnly(10, 45), 30));
    }

    // --- Parse ------------------------------------------------------------

    [Fact]
    public void Reads_a_start_and_a_duration_beside_my_day()
    {
        var parsed = EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-07` `at:10:45` `for:45m`\n");

        Assert.Equal(new AgendaTime(new TimeOnly(10, 45), 45), parsed.AgendaTime);
        Assert.Empty(parsed.Unreadable!);
    }

    [Fact]
    public void A_start_alone_reads_as_thirty_minutes()
    {
        var parsed = EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-07` `at:09:00`\n");

        Assert.Equal(new AgendaTime(new TimeOnly(9, 0), 30), parsed.AgendaTime);
    }

    [Fact]
    public void A_duration_without_a_start_is_refused_as_malformed()
    {
        var parsed = EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-07` `for:45m`\n");

        Assert.Null(parsed.AgendaTime);
        Assert.Contains(new EntryTextParser.UnreadableToken("for", "45m"), parsed.Unreadable!);
    }

    [Theory]
    [InlineData("at:24:00", "at")]
    [InlineData("at:9:00", "at")]
    [InlineData("at:10.45", "at")]
    [InlineData("at:", "at")]
    [InlineData("for:0m", "for")]
    [InlineData("for:45", "for")]
    [InlineData("for:-5m", "for")]
    [InlineData("for:1.5m", "for")]
    public void A_value_that_does_not_read_is_refused_and_named(string token, string name)
    {
        var parsed = EntryTextParser.Parse($"# Standup\n`task` `myday:2026-10-07` `at:10:00` `{token}`\n");

        Assert.Contains(parsed.Unreadable!, unreadable => unreadable.Name == name);
    }

    [Fact]
    public void A_refused_duration_beside_a_good_start_still_leaves_the_start_at_thirty_minutes()
    {
        var parsed = EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-07` `at:10:00` `for:soon`\n");

        Assert.Equal(new AgendaTime(new TimeOnly(10, 0), 30), parsed.AgendaTime);
        Assert.Contains(new EntryTextParser.UnreadableToken("for", "soon"), parsed.Unreadable!);
    }

    // --- Write-back -------------------------------------------------------

    [Fact]
    public void The_canonical_line_writes_both_tokens_straight_after_my_day()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        var raw = EntryTextParser.ToRawText(entry.ToDto());

        Assert.Contains("`myday:2026-10-07` `at:10:45` `for:45m`", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void The_canonical_line_writes_no_duration_for_the_default()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(9, 0)));

        var raw = EntryTextParser.ToRawText(entry.ToDto());

        Assert.Contains("`myday:2026-10-07` `at:09:00`", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("for:", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_to_task_to_text_lands_the_line_back_as_it_was()
    {
        const string raw = "# Standup\n`task` `*medium` `!draft` `myday:2026-10-07` `at:10:45` `for:45m`\n";

        var entry = TaskEntryFields.CreateFrom(EntryTextParser.Parse(raw), order: 0);

        Assert.Equal(raw, EntryTextParser.ToRawText(entry.ToDto()));
    }

    [Fact]
    public void With_agenda_time_writes_after_my_day_and_clears()
    {
        const string raw = "# Standup\n`task` `myday:2026-10-07` `due:2026-10-09`\n";

        var written = EntryTextParser.WithAgendaTime(raw, new AgendaTime(new TimeOnly(14, 0), 90));
        Assert.Contains("`myday:2026-10-07` `at:14:00` `for:90m` `due:2026-10-09`", written, StringComparison.Ordinal);

        var cleared = EntryTextParser.WithAgendaTime(written, null);
        Assert.Equal(raw, cleared);
    }

    [Fact]
    public void With_agenda_time_writes_nothing_without_a_my_day_date()
    {
        const string raw = "# Standup\n`task`\n";

        Assert.Equal(raw, EntryTextParser.WithAgendaTime(raw, new AgendaTime(new TimeOnly(14, 0), 90)));
    }

    [Fact]
    public void With_my_day_on_the_same_day_keeps_the_agenda_time()
    {
        const string raw = "# Standup\n`task` `myday:2026-10-07` `at:10:45` `for:45m`\n";

        Assert.Equal(raw, EntryTextParser.WithMyDay(raw, Today));
    }

    [Fact]
    public void Moving_my_day_to_another_day_drops_both_tokens()
    {
        const string raw = "# Standup\n`task` `myday:2026-10-07` `at:10:45` `for:45m`\n";

        var moved = EntryTextParser.WithMyDay(raw, Today.AddDays(1));

        Assert.Equal("# Standup\n`task` `myday:2026-10-08`\n", moved);
    }

    [Fact]
    public void Clearing_my_day_drops_both_tokens()
    {
        const string raw = "# Standup\n`task` `myday:2026-10-07` `at:10:45` `for:45m`\n";

        Assert.Equal("# Standup\n`task`\n", EntryTextParser.WithMyDay(raw, null));
    }

    // --- The aggregate ----------------------------------------------------

    [Fact]
    public void A_task_holds_no_agenda_time_outside_my_day()
    {
        var entry = new TaskItem("Standup", string.Empty, EntryType.Task, Priority.Medium);

        entry.SetAgendaTime(new AgendaTime(new TimeOnly(10, 45), 45));

        Assert.Null(entry.AgendaTime);
    }

    [Fact]
    public void Moving_the_task_to_another_day_drops_its_slot()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        entry.SetInMyDayOn(Today.AddDays(1));

        Assert.Null(entry.AgendaTime);
    }

    [Fact]
    public void Taking_the_task_out_of_my_day_drops_its_slot()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        entry.SetInMyDayOn(null);

        Assert.Null(entry.AgendaTime);
    }

    [Fact]
    public void Setting_the_same_day_again_keeps_the_slot()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        entry.SetInMyDayOn(Today);

        Assert.Equal(new AgendaTime(new TimeOnly(10, 45), 45), entry.AgendaTime);
    }

    // --- The text save ----------------------------------------------------

    [Fact]
    public void A_save_that_removes_my_day_drops_the_slot_left_on_the_line()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        TaskEntryFields.ApplyToExisting(entry, EntryTextParser.Parse("# Standup\n`task` `at:10:45` `for:45m`\n"));

        Assert.Null(entry.InMyDayOn);
        Assert.Null(entry.AgendaTime);
        Assert.DoesNotContain("at:", EntryTextParser.ToRawText(entry.ToDto()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_that_changes_the_day_drops_the_slot_carried_along_unedited()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        TaskEntryFields.ApplyToExisting(
            entry,
            EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-08` `at:10:45` `for:45m`\n"));

        Assert.Equal(Today.AddDays(1), entry.InMyDayOn);
        Assert.Null(entry.AgendaTime);
    }

    [Fact]
    public void A_save_that_changes_the_day_and_types_a_new_slot_keeps_the_new_slot()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        TaskEntryFields.ApplyToExisting(
            entry,
            EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-08` `at:14:00`\n"));

        Assert.Equal(new AgendaTime(new TimeOnly(14, 0), 30), entry.AgendaTime);
    }

    [Fact]
    public void A_save_that_deletes_the_tokens_clears_the_slot()
    {
        var entry = InMyDay(new AgendaTime(new TimeOnly(10, 45), 45));

        TaskEntryFields.ApplyToExisting(entry, EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-07`\n"));

        Assert.Equal(Today, entry.InMyDayOn);
        Assert.Null(entry.AgendaTime);
    }

    [Fact]
    public void A_save_of_a_lone_duration_sets_no_slot()
    {
        var entry = new TaskItem("Standup", string.Empty, EntryType.Task, Priority.Medium);

        TaskEntryFields.ApplyToExisting(entry, EntryTextParser.Parse("# Standup\n`task` `myday:2026-10-07` `for:45m`\n"));

        Assert.Null(entry.AgendaTime);
    }

    private static TaskItem InMyDay(AgendaTime agendaTime)
    {
        var entry = new TaskItem("Standup", string.Empty, EntryType.Task, Priority.Medium);
        entry.SetInMyDayOn(Today);
        entry.SetAgendaTime(agendaTime);
        return entry;
    }
}
