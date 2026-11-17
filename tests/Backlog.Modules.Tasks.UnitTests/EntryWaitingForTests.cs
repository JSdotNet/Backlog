using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// A task's waiting-for — who it waits on and when to follow up — as the
/// <c>waiting:</c> and <c>followup:</c> tokens (local ADR 0054 §4,
/// <c>.devbook/design/content-editing.md#scheduling-and-dependency-tokens</c>).
/// </summary>
public class EntryWaitingForTests
{
    [Fact]
    public void Waiting_and_followup_are_read_off_the_metadata_line()
    {
        var parsed = EntryTextParser.Parse("# Fix the roof\n`task` `!ready` `waiting:Jansen Roofing` `followup:2026-11-20`\n");

        Assert.Equal(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)), parsed.WaitingFor);
        Assert.Equal(EntryStatus.Ready, parsed.Status);
        Assert.Empty(parsed.Unreadable!);
    }

    [Fact]
    public void Waiting_alone_is_a_waiting_for_with_no_follow_up_date()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `waiting:Anna`\n");

        Assert.Equal("Anna", parsed.WaitingFor?.Who);
        Assert.Null(parsed.WaitingFor?.FollowUpOn);
    }

    [Fact]
    public void A_line_without_the_tokens_waits_on_nobody()
    {
        Assert.Null(EntryTextParser.Parse("# Title\n`task` `!ready`\n").WaitingFor);
    }

    /// <summary>A date with nobody to chase is not a waiting-for, the way <c>for:</c>
    /// without <c>at:</c> is not an agenda time.</summary>
    [Fact]
    public void A_followup_without_waiting_is_refused_and_named()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `followup:2026-11-20`\n");

        Assert.Null(parsed.WaitingFor);
        Assert.Contains(parsed.Unreadable!, token => token.Name == "followup" && token.Value == "2026-11-20");
    }

    [Fact]
    public void An_unreadable_followup_date_is_refused_and_keeps_who()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `waiting:Anna` `followup:next-week`\n");

        Assert.Equal(new WaitingFor("Anna"), parsed.WaitingFor);
        Assert.Contains(parsed.Unreadable!, token => token.Name == "followup" && token.Value == "next-week");
    }

    [Fact]
    public void An_empty_waiting_token_is_refused()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `waiting:`\n");

        Assert.Null(parsed.WaitingFor);
        Assert.Contains(parsed.Unreadable!, token => token.Name == "waiting");
    }

    [Fact]
    public void Setting_writes_both_tokens_and_clearing_takes_both_off()
    {
        const string raw = "# Title\n`task` `!ready` `due:2026-08-21`\n";

        var waiting = EntryTextParser.WithWaitingFor(raw, new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)));
        Assert.Equal("# Title\n`task` `!ready` `due:2026-08-21` `waiting:Jansen Roofing` `followup:2026-11-20`\n", waiting);

        // Clearing the waiting-for takes the follow-up date with it and leaves the
        // line exactly as it was.
        Assert.Equal(raw, EntryTextParser.WithWaitingFor(waiting, null));
    }

    [Fact]
    public void Changing_who_keeps_one_pair_of_tokens()
    {
        const string raw = "# Title\n`task` `waiting:Anna` `followup:2026-11-20` `effort:3`\n";

        var changed = EntryTextParser.WithWaitingFor(raw, new WaitingFor("Bob"));

        Assert.Equal("# Title\n`task` `effort:3` `waiting:Bob`\n", changed);
    }

    /// <summary>The canonical line writes it after the block and before the entries
    /// it waits on, and a parse-write-parse round trip lands the same line twice.</summary>
    [Fact]
    public void The_waiting_for_survives_the_canonical_rewrite()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task, Priority.Medium);
        entry.SetBlockedSince(new DateOnly(2026, 10, 5));
        entry.SetWaitingFor(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)));
        entry.SetDependsOn(["a1b2c3"]);

        var rewritten = EntryTextParser.ToRawText(entry.ToDto());

        Assert.Contains(
            "`blocked:2026-10-05` `waiting:Jansen Roofing` `followup:2026-11-20` `after:a1b2c3`",
            rewritten,
            StringComparison.Ordinal);

        var reparsed = new TaskItem("Title", string.Empty, EntryType.Task, Priority.Medium);
        TaskEntryFields.ApplyToExisting(reparsed, EntryTextParser.Parse(rewritten));

        Assert.Equal(entry.WaitingFor, reparsed.WaitingFor);
        Assert.Equal(rewritten, EntryTextParser.ToRawText(reparsed.ToDto()));
    }

    [Fact]
    public void Deleting_the_waiting_token_clears_the_task_field()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task, Priority.Medium);
        entry.SetWaitingFor(new WaitingFor("Anna", new DateOnly(2026, 11, 20)));

        TaskEntryFields.ApplyToExisting(entry, EntryTextParser.Parse("# Title\n`task` `!ready`\n"));

        Assert.Null(entry.WaitingFor);
    }

    /// <summary>A passed follow-up date reads overdue the way a passed reminder does:
    /// the day after it, never on it, and never without a date.</summary>
    [Fact]
    public void A_passed_follow_up_date_reads_overdue()
    {
        var waiting = new WaitingFor("Anna", new DateOnly(2026, 11, 20));

        Assert.False(waiting.IsFollowUpOverdue(new DateOnly(2026, 11, 19)));
        Assert.False(waiting.IsFollowUpOverdue(new DateOnly(2026, 11, 20)));
        Assert.True(waiting.IsFollowUpOverdue(new DateOnly(2026, 11, 21)));
        Assert.False(new WaitingFor("Anna").IsFollowUpOverdue(new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Who_is_trimmed_and_a_blank_or_backtick_name_is_no_waiting_for()
    {
        Assert.Equal("Anna", new WaitingFor("  Anna  ").Who);
        Assert.Null(WaitingFor.From("  ", new DateOnly(2026, 11, 20)));
        Assert.Null(WaitingFor.From("An`na", null));
        Assert.Throws<ArgumentException>(() => new WaitingFor(" "));
    }
}
