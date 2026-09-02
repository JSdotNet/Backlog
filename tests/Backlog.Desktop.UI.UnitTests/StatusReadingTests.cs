using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Direct metadata edits can jump to any status, while the domain lifecycle
/// graph still documents the guided transition flow for callers that need it.
/// </summary>
public class StatusReadingTests
{
    private static EntryRow RowAt(EntryStatus saved, string typedStatusToken) => new()
    {
        Status = saved,
        RawText = $"# A thing\n`task` `{typedStatusToken}`\n"
    };

    [Fact]
    public void A_reachable_status_is_previewed()
    {
        var row = RowAt(EntryStatus.Draft, "!ready");

        Assert.Equal(EntryStatus.Ready, row.PreviewStatus);
        Assert.Null(row.BlockedStatus);
    }

    [Fact]
    public void A_skipped_status_is_previewed()
    {
        var row = RowAt(EntryStatus.Draft, "!in-progress");

        Assert.Equal(EntryStatus.InProgress, row.PreviewStatus);
        Assert.Null(row.BlockedStatus);
    }

    [Fact]
    public void Restating_the_current_status_is_not_a_refusal()
    {
        var row = RowAt(EntryStatus.InProgress, "!in-progress");

        Assert.Equal(EntryStatus.InProgress, row.PreviewStatus);
        Assert.Null(row.BlockedStatus);
    }

    [Fact]
    public void A_skipped_status_is_read_without_a_refusal_note()
    {
        var row = RowAt(EntryStatus.Draft, "!done");

        var status = row.MetaReadings.Single(r => r.Kind == "status");

        Assert.Equal("done", status.Value);
        Assert.Null(status.Note);
    }

    [Fact]
    public void An_accepted_status_carries_no_note()
    {
        var row = RowAt(EntryStatus.Draft, "!ready");

        var status = row.MetaReadings.Single(r => r.Kind == "status");

        Assert.Equal("ready", status.Value);
        Assert.Null(status.Note);
    }

    // The scheduling and dependency tokens read back too. They save either way;
    // what these are about is the entry being *restated* to the reader, per
    // .design/content-editing.md#live-parse-confirmation — a field that saved
    // silently was a field nobody could check before trusting it.

    private static EntryRow RowWith(string metadata) => new()
    {
        RawText = $"# A thing\n`task` {metadata}\n"
    };

    [Fact]
    public void The_scheduling_and_dependency_tokens_are_all_read_back()
    {
        var row = RowWith("`due:2026-08-21` `remind:2026-08-21T09:00` `repeat:weekly` `myday:2026-08-19` `after:a1b2c3`");

        var readings = row.MetaReadings.ToDictionary(reading => reading.Kind, reading => reading.Value);

        Assert.Equal("2026-08-21", readings["due"]);
        Assert.Equal("2026-08-21T09:00", readings["reminder"]);
        Assert.Equal("weekly", readings["repeat"]);
        Assert.Equal("2026-08-19", readings["my day"]);
        Assert.Equal("a1b2c3", readings["after"]);
    }

    /// <summary>Absent means absent. An unset scheduling field contributes no
    /// reading rather than one saying "none": there is no default due date for a
    /// reader to mistake for something they asked for.</summary>
    [Fact]
    public void An_unset_scheduling_field_says_nothing()
    {
        var kinds = RowWith("`*high`").MetaReadings.Select(reading => reading.Kind).ToList();

        Assert.DoesNotContain("due", kinds);
        Assert.DoesNotContain("reminder", kinds);
        Assert.DoesNotContain("repeat", kinds);
        Assert.DoesNotContain("my day", kinds);
        Assert.DoesNotContain("after", kinds);
    }

    /// <summary>Every one of them is explicit, because none of them has a default
    /// to be the quiet version of.</summary>
    [Fact]
    public void A_scheduling_reading_is_always_something_the_reader_typed()
    {
        var row = RowWith("`due:2026-08-21` `repeat:weekly`");

        Assert.All(
            row.MetaReadings.Where(reading => reading.Kind is "due" or "repeat"),
            reading => Assert.True(reading.Explicit));
    }

    /// <summary>An entry waiting on two things names both. Order carries no
    /// meaning, but neither of them may go missing.</summary>
    [Fact]
    public void Every_dependency_gets_its_own_reading()
    {
        var row = RowWith("`after:a1b2c3` `after:d4e5f6`");

        Assert.Equal(
            ["a1b2c3", "d4e5f6"],
            row.MetaReadings.Where(reading => reading.Kind == "after").Select(reading => reading.Value));
    }

    [Theory]
    [InlineData("`due:friday`", "due", "friday")]
    [InlineData("`remind:09:00`", "reminder", "09:00")]
    [InlineData("`repeat:fortnightly`", "repeat", "fortnightly")]
    [InlineData("`myday:tomorrow`", "my day", "tomorrow")]
    public void A_value_the_parser_refuses_reads_as_refused(string metadata, string kind, string value)
    {
        var reading = Assert.Single(RowWith(metadata).MetaReadings, r => r.Kind == kind);

        // The words the reader typed, and a note saying they did not land. The
        // field is unset either way; the difference is whether they can tell.
        Assert.Equal(value, reading.Value);
        Assert.NotNull(reading.Note);
        Assert.True(reading.Explicit);
    }

    /// <summary>A token with nothing after the colon is malformed rather than
    /// "no due date", so it is refused rather than treated as absent — and it
    /// reads as something rather than as an empty gap on the hint line.</summary>
    [Fact]
    public void An_empty_named_token_is_refused_rather_than_treated_as_absent()
    {
        var reading = Assert.Single(RowWith("`due:`").MetaReadings, r => r.Kind == "due");

        Assert.Equal("(empty)", reading.Value);
        Assert.NotNull(reading.Note);
    }

    /// <summary>A refused value and an accepted one are filed under the same kind,
    /// so "due" appears once on the hint line either way rather than under two
    /// different words depending on whether it parsed.</summary>
    [Fact]
    public void A_refused_value_is_filed_under_the_kind_it_would_have_set()
    {
        var refused = RowWith("`remind:soon`").MetaReadings.Select(reading => reading.Kind);
        var accepted = RowWith("`remind:2026-08-21T09:00`").MetaReadings.Select(reading => reading.Kind);

        Assert.Contains("reminder", refused);
        Assert.Contains("reminder", accepted);
    }

    /// <summary>One bad token does not take the line down with it. Refusing the
    /// whole metadata line would lose the fields around the one that failed.</summary>
    [Fact]
    public void A_refused_value_leaves_the_tokens_around_it_alone()
    {
        var row = RowWith("`*high` `!ready` `due:friday` `repeat:weekly`");

        var readings = row.MetaReadings.ToDictionary(reading => reading.Kind, reading => reading.Value);

        Assert.Equal("high", readings["priority"]);
        Assert.Equal("ready", readings["status"]);
        Assert.Equal("weekly", readings["repeat"]);
        Assert.Null(row.PreviewDueOn);
    }

    [Theory]
    [InlineData(EntryStatus.Draft, EntryStatus.Ready, true)]
    [InlineData(EntryStatus.Draft, EntryStatus.InProgress, false)]
    [InlineData(EntryStatus.Draft, EntryStatus.Draft, true)]
    [InlineData(EntryStatus.Archived, EntryStatus.Draft, true)]
    [InlineData(EntryStatus.Done, EntryStatus.Draft, false)]
    public void The_lifecycle_graph_is_what_the_readme_documents(EntryStatus from, EntryStatus to, bool allowed)
        => Assert.Equal(allowed, TaskItem.IsTransitionAllowed(from, to));

    [Fact]
    public void Every_status_has_somewhere_to_go()
    {
        // A dead-end status would strand guided lifecycle callers with no next step.
        foreach (var status in Enum.GetValues<EntryStatus>())
        {
            Assert.NotEmpty(TaskItem.NextStatusesFrom(status));
        }
    }
}
