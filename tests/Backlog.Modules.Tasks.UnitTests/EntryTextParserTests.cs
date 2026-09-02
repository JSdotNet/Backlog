using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;


namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The parser is the contract between what a person types into an entry and
/// what gets stored, so these tests are written as the text someone would
/// actually type — including the half-finished and slightly-wrong spellings
/// that a forgiving editor has to survive.
/// </summary>
public class EntryTextParserTests
{
    // --- Title, meta line, body ------------------------------------------

    [Fact]
    public void Reads_the_title_from_a_heading()
    {
        var parsed = EntryTextParser.Parse("# Ship the importer\n");

        Assert.Equal("Ship the importer", parsed.Title);
    }

    [Fact]
    public void Reads_a_title_typed_without_the_hash()
    {
        var parsed = EntryTextParser.Parse("Ship the importer\n");

        Assert.Equal("Ship the importer", parsed.Title);
    }

    [Fact]
    public void An_empty_heading_line_reads_as_no_title_rather_than_a_literal_hash()
    {
        // What a brand-new, not-yet-titled entry starts as: the heading marker
        // with nothing typed after it yet. Trimming the line loses the space
        // that tells "# " apart from a title that is just "#", so this used to
        // come back as the literal character rather than an empty title.
        var parsed = EntryTextParser.Parse("# \n`task` `*medium` `!draft`\n");

        Assert.Equal(string.Empty, parsed.Title);
    }

    [Fact]
    public void Reads_type_priority_and_status_from_the_meta_line()
    {
        var parsed = EntryTextParser.Parse("# Title\n`idea` `high` `ready`\n");

        Assert.Equal(EntryType.Idea, parsed.Type);
        Assert.Equal(Priority.High, parsed.Priority);
        Assert.Equal(EntryStatus.Ready, parsed.Status);
    }

    [Theory]
    [InlineData("in-progress")]
    [InlineData("in progress")]
    [InlineData("in_progress")]
    [InlineData("InProgress")]
    public void Accepts_any_reasonable_spelling_of_a_status(string token)
    {
        var parsed = EntryTextParser.Parse($"# Title\n`{token}`\n");

        Assert.Equal(EntryStatus.InProgress, parsed.Status);
    }

    [Fact]
    public void Leaves_unknown_meta_tokens_unset_rather_than_failing()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `banana`\n");

        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Null(parsed.Priority);
        Assert.Null(parsed.Status);
    }

    [Fact]
    public void Does_not_mistake_a_prose_line_for_a_meta_line()
    {
        var parsed = EntryTextParser.Parse("# Title\nUse the `dotnet` CLI for this.\n");

        Assert.Null(parsed.Type);
        Assert.Equal("Use the `dotnet` CLI for this.", parsed.Body);
    }

    [Fact]
    public void Collects_tags_from_anywhere_in_the_body()
    {
        var parsed = EntryTextParser.Parse("# Title\n\nSomething #alpha and later #beta and #alpha again.\n");

        Assert.Equal(["alpha", "beta"], parsed.Tags);
    }

    // --- Tags typed in the title -----------------------------------------
    //
    // A title may carry tags inline: `@name` is a person, `#name` is a general
    // tag. The title text is kept exactly as typed — nothing is stripped or
    // rewritten — so the sigils stay visible and the tags are *derived* from
    // them, the same way body tags always have been.

    [Fact]
    public void Reads_a_person_tag_and_a_general_tag_from_the_title()
    {
        var parsed = EntryTextParser.Parse("# Ship the installer @bob #deploy\n");

        // The title is preserved verbatim: the tags are read off it, not out of it.
        Assert.Equal("Ship the installer @bob #deploy", parsed.Title);
        Assert.Equal(["@bob", "deploy"], parsed.Tags);
    }

    [Fact]
    public void Title_tags_are_derived_and_never_metadata_tags()
    {
        var parsed = EntryTextParser.Parse("# Ship the installer @bob #deploy\n`task` `#release`\n");

        // Only what was authored on the backtick line is a metadata tag; the
        // title's tags join the derived union beside the body's.
        Assert.Equal(["release"], parsed.MetadataTags);
        Assert.Equal(["release", "@bob", "deploy"], parsed.Tags);
    }

    [Fact]
    public void A_person_tag_and_a_general_tag_of_the_same_name_are_distinct()
    {
        var parsed = EntryTextParser.Parse("# Ask @bob about #bob\n");

        Assert.Equal(["@bob", "bob"], parsed.Tags);
    }

    [Fact]
    public void An_email_address_in_the_title_is_not_a_tag()
    {
        // The `(?<!\S)` guard is the whole reason this holds: a sigil only starts
        // a tag when nothing is welded to its left.
        var parsed = EntryTextParser.Parse("# Mail bob@example.com about it\n");

        Assert.Empty(parsed.Tags);
    }

    [Fact]
    public void A_title_tag_is_lower_cased_like_every_other_tag()
    {
        var parsed = EntryTextParser.Parse("# Ship it @Bob #Deploy\n");

        Assert.Equal(["@bob", "deploy"], parsed.Tags);
    }

    [Fact]
    public void Stored_tags_without_a_sigil_still_read_as_general_tags()
    {
        // Everything written before person tags existed keeps its meaning: an
        // unsigilled tag is a general tag, with no migration and no backfill.
        var parsed = EntryTextParser.Parse("# Title\n`task` `#deploy` `#alpha`\n");

        Assert.Equal(["deploy", "alpha"], parsed.MetadataTags);
        Assert.Equal(["deploy", "alpha"], parsed.Tags);
    }

    [Fact]
    public void A_sub_item_heading_is_not_scanned_for_person_tags()
    {
        // Entry title only. A `##` or `###` heading is a sub-item's own title and
        // says nothing about who the entry is for.
        var parsed = EntryTextParser.Parse("# Ship it\n\n## Deploy @bob\n\n### Verify @carol\n");

        Assert.Empty(parsed.Tags);
    }

    [Fact]
    public void Body_prose_does_not_learn_the_person_sigil()
    {
        // Body prose keeps recognising `#tag` and only `#tag`, exactly as before.
        var parsed = EntryTextParser.Parse("# Ship it\n\nAsk @bob to look at #deploy.\n");

        Assert.Equal(["deploy"], parsed.Tags);
    }

    [Fact]
    public void Raw_text_never_writes_a_person_tag_on_to_the_metadata_line()
    {
        // `@` means Area on the metadata line, and the tag loop writes `#{tag}`.
        // A person tag reaching it would come out as the corrupt token `#@bob`.
        var entry = new TaskItem("Ship the installer @bob #deploy", string.Empty, EntryType.Task, Priority.Medium);
        entry.SetTags(["@bob", "deploy"]);

        var raw = EntryTextParser.ToRawText(entry.ToDto());

        Assert.DoesNotContain("@bob`", raw, StringComparison.Ordinal);
        Assert.Contains("`#deploy`", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void A_person_tag_round_trips_through_raw_text_without_being_dropped_or_doubled()
    {
        // Nothing is lost by keeping the person tag off the metadata line: the
        // preserved title text is what carries it, and re-reading finds it again.
        var entry = new TaskItem("Ship the installer @bob #deploy", string.Empty, EntryType.Task, Priority.Medium);
        entry.SetTags(["@bob", "deploy"]);

        var once = EntryTextParser.Parse(EntryTextParser.ToRawText(entry.ToDto()));

        var reloaded = new TaskItem(once.Title, once.Body, EntryType.Task, Priority.Medium);
        reloaded.SetTags(once.Tags);
        var twice = EntryTextParser.Parse(EntryTextParser.ToRawText(reloaded.ToDto()));

        Assert.Equal("Ship the installer @bob #deploy", once.Title);
        Assert.Equal(["deploy", "@bob"], once.Tags);
        Assert.Equal(once.Title, twice.Title);
        Assert.Equal(once.Tags, twice.Tags);
    }

    [Fact]
    public void The_tag_editor_round_trips_a_person_tag()
    {
        // NormalizeTags validates each tag against the general-tag shape, so a
        // person tag has to be recognised there or the editor silently drops it.
        var parsed = EntryTextParser.ParseTagsInput("@Bob, deploy #release");

        Assert.Equal(["@bob", "deploy", "release"], parsed);
        Assert.Equal("@bob deploy release", EntryTextParser.FormatTagsInput(parsed));
    }

    [Fact]
    public void Writing_tags_on_to_the_metadata_line_leaves_the_person_tags_off_it()
    {
        // The person tag lives in the title; the metadata line only ever carries
        // the general ones, so `@` on that line still means Area and nothing else.
        var raw = EntryTextParser.WithTags("# Ship it @bob\n`task` `@repos`\n", "@bob deploy");

        Assert.Contains("`#deploy`", raw, StringComparison.Ordinal);
        Assert.Contains("`@repos`", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("#@bob", raw, StringComparison.Ordinal);
        Assert.Equal("repos", EntryTextParser.Parse(raw).Area);
    }

    // --- Area -------------------------------------------------------------

    [Fact]
    public void Reads_the_area_from_the_meta_line()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `@repos`\n");

        Assert.Equal("repos", parsed.Area);
        Assert.Equal(EntryType.Task, parsed.Type);
    }

    [Fact]
    public void Area_is_free_form_and_lower_cased()
    {
        var parsed = EntryTextParser.Parse("# Title\n`@Client Work`\n");

        Assert.Equal("client work", parsed.Area);
    }

    [Fact]
    public void Leaves_the_area_unset_when_none_is_typed()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task`\n");

        Assert.Null(parsed.Area);
    }

    // --- Scheduling and dependency tokens --------------------------------

    [Fact]
    public void Reads_the_scheduling_and_dependency_tokens_from_the_meta_line()
    {
        var parsed = EntryTextParser.Parse(
            "# Deploy SpecManager\n"
            + "`task` `*high` `!ready` `@repos` `#deploy` `due:2026-08-21` "
            + "`remind:2026-08-21T09:00` `repeat:weekly` `myday:2026-08-19` `after:a1b2c3`\n");

        Assert.Equal(new DateOnly(2026, 8, 21), parsed.DueOn);
        Assert.Equal(new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Unspecified), parsed.RemindAt);
        Assert.Equal(new Recurrence(1, RecurrenceUnit.Week), parsed.Recurrence);
        Assert.Equal(new DateOnly(2026, 8, 19), parsed.InMyDayOn);
        Assert.Equal(["a1b2c3"], parsed.DependsOn);

        // The named tokens sit on the same line as the sigils and must not have
        // disturbed them on the way past.
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Equal(Priority.High, parsed.Priority);
        Assert.Equal(EntryStatus.Ready, parsed.Status);
        Assert.Equal("repos", parsed.Area);
        Assert.Equal(["deploy"], parsed.MetadataTags);
    }

    /// <summary>A reminder carries no zone on purpose: 09:00 is 09:00 wherever the
    /// reader is when it arrives, so the parsed value must be unzoned rather than
    /// pinned to whichever machine read the file.</summary>
    [Fact]
    public void A_reminder_is_read_as_an_unzoned_wall_clock_time()
    {
        var parsed = EntryTextParser.Parse("# Title\n`remind:2026-08-21T09:00`\n");

        Assert.Equal(DateTimeKind.Unspecified, parsed.RemindAt!.Value.Kind);
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(parsed.RemindAt.Value));
    }

    [Theory]
    [InlineData("daily", 1, RecurrenceUnit.Day)]
    [InlineData("weekly", 1, RecurrenceUnit.Week)]
    [InlineData("monthly", 1, RecurrenceUnit.Month)]
    [InlineData("yearly", 1, RecurrenceUnit.Year)]
    [InlineData("2w", 2, RecurrenceUnit.Week)]
    [InlineData("3d", 3, RecurrenceUnit.Day)]
    [InlineData("6m", 6, RecurrenceUnit.Month)]
    [InlineData("2y", 2, RecurrenceUnit.Year)]
    public void A_repeat_is_read_as_an_interval_and_a_unit(string token, int interval, RecurrenceUnit unit)
    {
        var parsed = EntryTextParser.Parse($"# Title\n`repeat:{token}`\n");

        Assert.Equal(new Recurrence(interval, unit), parsed.Recurrence);
        Assert.Null(parsed.Recurrence!.Weekdays);
    }

    /// <summary>"Every weekday" is a week-shaped repeat restricted to Monday
    /// through Friday, not a fifth unit — which is what keeps
    /// <c>RecurrenceUnit</c> down to the four periods a calendar has.</summary>
    [Fact]
    public void Weekdays_is_a_weekly_repeat_restricted_to_the_working_week()
    {
        var parsed = EntryTextParser.Parse("# Title\n`repeat:weekdays`\n");

        Assert.Equal(1, parsed.Recurrence!.Interval);
        Assert.Equal(RecurrenceUnit.Week, parsed.Recurrence.Unit);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            parsed.Recurrence.Weekdays!);
    }

    [Fact]
    public void Dependency_tokens_repeat_and_collect_in_the_order_written()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `after:a1b2c3` `after:d4e5f6` `after:a1b2c3`\n");

        // Two mentions of the same predecessor are one dependency; the order the
        // rest were written in is kept because reshuffling it would churn the file
        // for nothing.
        Assert.Equal(["a1b2c3", "d4e5f6"], parsed.DependsOn);
    }

    [Fact]
    public void A_dependency_id_that_names_nothing_is_still_a_dependency()
    {
        // Ids are opaque strings, the same rule repo ids follow. Dropping the
        // ones that resolve to nothing would let a chain claim to be ready when
        // the step it waits on is merely missing from view.
        var parsed = EntryTextParser.Parse("# Title\n`after:whatever-this-is`\n");

        Assert.Equal(["whatever-this-is"], parsed.DependsOn);
    }

    [Theory]
    [InlineData("due:not-a-date")]
    [InlineData("due:2026-13-45")]
    [InlineData("due:")]
    [InlineData("remind:2026-08-21")]
    [InlineData("myday:21/08/2026")]
    [InlineData("repeat:fortnightly")]
    [InlineData("repeat:0w")]
    [InlineData("after:")]
    [InlineData("id:")]
    [InlineData("repo:")]
    public void A_malformed_named_token_leaves_its_field_unset_rather_than_failing(string token)
    {
        var parsed = EntryTextParser.Parse($"# Title\n`task` `{token}`\n");

        // Exactly what an unknown sigil already does. Refusing the line would
        // lose the fields around the bad token, and refusing the save would lose
        // the entry.
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Null(parsed.DueOn);
        Assert.Null(parsed.RemindAt);
        Assert.Null(parsed.Recurrence);
        Assert.Null(parsed.InMyDayOn);
        Assert.Empty(parsed.DependsOn!);
        Assert.Null(parsed.ImportItemId);
        Assert.Empty(parsed.RepoIds!);
    }

    // --- Import tokens: id and repo ---------------------------------------
    //
    // Neither is Import-specific: `id:` names an entry before it has a real
    // backlog_item_id and `repo:` targets a repository, exactly as
    // .design/content-editing.md#scheduling-and-dependency-tokens describes them
    // for any pasted batch, hand-typed or brought in through Import alike.

    [Fact]
    public void An_id_token_parses_to_the_local_id()
    {
        var parsed = EntryTextParser.Parse("# Add the import command\n`task` `id:add-command`\n");

        Assert.Equal("add-command", parsed.ImportItemId);
    }

    [Fact]
    public void A_repeated_id_token_keeps_only_the_last_one()
    {
        // Last-one-wins, like `due:` — an entry names itself once.
        var parsed = EntryTextParser.Parse("# Title\n`id:first` `id:second`\n");

        Assert.Equal("second", parsed.ImportItemId);
    }

    [Fact]
    public void A_missing_id_token_leaves_the_local_id_unset()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task`\n");

        Assert.Null(parsed.ImportItemId);
    }

    [Fact]
    public void A_repo_token_parses_to_the_target_repository()
    {
        var parsed = EntryTextParser.Parse("# Deploy SpecManager\n`task` `repo:specmanager`\n");

        Assert.Equal(["specmanager"], parsed.RepoIds);
    }

    /// <summary>
    /// A <c>repo:</c> value containing a <c>/</c> lexes verbatim, because the
    /// token grammar splits on the FIRST colon only. That is what lets
    /// <c>repo_ids</c> hold an <c>owner/name</c> identity without the parser
    /// needing a grammar change or an opinion about the value — which per ADR 0002
    /// it may not have, living in <c>.Abstractions</c> where no registry is
    /// visible.
    /// </summary>
    [Fact]
    public void A_repo_token_carrying_an_owner_and_name_lexes_verbatim()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `repo:JSdotNet/Backlog`\n");

        Assert.Equal(["JSdotNet/Backlog"], parsed.RepoIds);
    }

    [Fact]
    public void Repo_tokens_repeat_and_collect_in_the_order_written()
    {
        var parsed = EntryTextParser.Parse("# Title\n`repo:alpha` `repo:beta` `repo:alpha`\n");

        // Two mentions of the same repository are one target, the same rule
        // `after:` follows.
        Assert.Equal(["alpha", "beta"], parsed.RepoIds);
    }

    [Fact]
    public void A_missing_repo_token_leaves_the_repositories_empty()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task`\n");

        Assert.Empty(parsed.RepoIds!);
    }

    [Fact]
    public void The_canonical_form_carries_the_id_and_repo_tokens_after_the_dependencies()
    {
        var entry = new TaskItem("Deploy SpecManager", string.Empty, EntryType.Task, Priority.High);
        entry.SetDependsOn(["a1b2c3"]);
        entry.SetImportItemId("deploy-specmanager");
        entry.SetRepoIds(["specmanager", "backlog-desktop"]);

        var raw = EntryTextParser.ToRawText(entry.ToDto());

        Assert.Equal(
            "`task` `*high` `!draft` `after:a1b2c3` `id:deploy-specmanager` `repo:specmanager` `repo:backlog-desktop`",
            raw.Split('\n')[1]);
    }

    [Fact]
    public void WithRepoIds_writes_the_repo_tokens_on_to_the_metadata_line()
    {
        var raw = EntryTextParser.WithRepoIds("# Deploy SpecManager\n`task` `@repos`\n", ["specmanager"]);

        // The area is untouched: it is the person's own pile, and naming a
        // repository is not a filing decision about it.
        Assert.Equal("`task` `@repos` `repo:specmanager`", raw.Split('\n')[1]);
    }

    [Fact]
    public void WithRepoIds_replaces_the_whole_set_rather_than_adding_to_it()
    {
        var raw = EntryTextParser.WithRepoIds("# Title\n`task` `repo:alpha` `repo:beta`\n", ["gamma"]);

        Assert.Equal("`task` `repo:gamma`", raw.Split('\n')[1]);
    }

    [Fact]
    public void WithRepoIds_clears_the_tokens_when_handed_nothing()
    {
        var raw = EntryTextParser.WithRepoIds("# Title\n`task` `@repos` `repo:alpha`\n", []);

        // Clearing has to be expressible for the reason every other named token
        // gives: an unset field carries no token rather than an empty one, so
        // "no repository" and "no token" are the same state.
        Assert.Equal("`task` `@repos`", raw.Split('\n')[1]);
    }

    [Fact]
    public void WithRepoIds_keeps_every_target_it_is_handed_and_drops_the_repeats()
    {
        var raw = EntryTextParser.WithRepoIds("# Title\n`task`\n", ["alpha", "beta", "alpha"]);

        // An entry may target more than one repository, so this writes a set
        // rather than a value. The de-duplication matches the parser's: two
        // mentions of one repository are one target.
        Assert.Equal("`task` `repo:alpha` `repo:beta`", raw.Split('\n')[1]);
    }

    [Fact]
    public void Id_and_repo_round_trip_through_a_parse_write_parse()
    {
        var once = EntryTextParser.Parse(
            "# Deploy SpecManager\n`task` `id:deploy-specmanager` `repo:specmanager` `repo:backlog-desktop`\n\nBody.\n");

        var entry = new TaskItem("Deploy SpecManager", once.Body, once.Type ?? EntryType.Task, once.Priority ?? Priority.Medium);
        entry.SetImportItemId(once.ImportItemId);
        entry.SetRepoIds(once.RepoIds ?? []);

        var raw = EntryTextParser.ToRawText(entry.ToDto());
        var twice = EntryTextParser.Parse(raw);

        Assert.Equal("deploy-specmanager", once.ImportItemId);
        Assert.Equal(once.ImportItemId, twice.ImportItemId);
        Assert.Equal(once.RepoIds, twice.RepoIds);
        // Writing the same text again lands the same line — no data lost on the
        // second trip through.
        Assert.Equal(raw, EntryTextParser.ToRawText(entry.ToDto()));
    }

    [Fact]
    public void Leaves_unknown_named_meta_tokens_unset_rather_than_failing()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `snooze:2026-08-21`\n");

        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Null(parsed.DueOn);
        Assert.Null(parsed.InMyDayOn);
    }

    /// <summary>
    /// An unrecognized named token is unrecognized, not invalid, so editing an
    /// unrelated field must not quietly delete it. This is the same
    /// backward-compatibility bargain the sigils already make.
    /// </summary>
    [Fact]
    public void An_unknown_named_token_survives_an_unrelated_field_edit()
    {
        const string raw = "# Title\n`task` `*medium` `!draft` `snooze:2026-08-21`\n";

        var rewritten = EntryTextParser.WithStatus(raw, EntryStatus.Ready);

        Assert.Contains("`snooze:2026-08-21`", rewritten, StringComparison.Ordinal);
        Assert.Contains("`!ready`", rewritten, StringComparison.Ordinal);
    }

    /// <summary>An area is free-form and may well contain a colon, so the named
    /// token rule must not reach past a sigil that already declared its
    /// kind.</summary>
    [Fact]
    public void A_sigilled_token_is_not_reread_as_a_named_token_because_it_holds_a_colon()
    {
        var parsed = EntryTextParser.Parse("# Title\n`@client:acme`\n");

        Assert.Equal("client:acme", parsed.Area);
    }

    [Fact]
    public void Writing_a_due_date_leaves_the_rest_of_the_meta_line_alone()
    {
        const string raw = "# Title\n`task` `*high` `!ready` `@repos` `#deploy`\n";

        var rewritten = EntryTextParser.WithDue(raw, new DateOnly(2026, 8, 21));

        Assert.Equal("# Title\n`task` `*high` `!ready` `@repos` `#deploy` `due:2026-08-21`\n", rewritten);
    }

    [Fact]
    public void Writing_a_due_date_twice_replaces_the_token_rather_than_repeating_it()
    {
        const string raw = "# Title\n`task` `due:2026-08-21`\n";

        var rewritten = EntryTextParser.WithDue(raw, new DateOnly(2026, 8, 28));

        Assert.Contains("`due:2026-08-28`", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-08-21", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Clearing_a_scheduling_field_removes_its_token_entirely()
    {
        // An unset field carries no token rather than an empty one: `due:` with
        // nothing after it is malformed, not "no due date".
        const string raw =
            "# Title\n`task` `due:2026-08-21` `remind:2026-08-21T09:00` `repeat:weekly` `myday:2026-08-19` `after:a1b2c3`\n";

        var cleared = EntryTextParser.WithDependsOn(
            EntryTextParser.WithMyDay(
                EntryTextParser.WithRepeat(
                    EntryTextParser.WithReminder(
                        EntryTextParser.WithDue(raw, null),
                        null),
                    null),
                null),
            []);

        Assert.Equal("# Title\n`task`\n", cleared);
    }

    [Fact]
    public void Each_scheduling_field_can_be_written_on_its_own()
    {
        const string raw = "# Title\n`task`\n";

        Assert.Contains("`remind:2026-08-21T09:00`", EntryTextParser.WithReminder(raw, new DateTime(2026, 8, 21, 9, 0, 0)), StringComparison.Ordinal);
        Assert.Contains("`repeat:2w`", EntryTextParser.WithRepeat(raw, new Recurrence(2, RecurrenceUnit.Week)), StringComparison.Ordinal);
        Assert.Contains("`myday:2026-08-19`", EntryTextParser.WithMyDay(raw, new DateOnly(2026, 8, 19)), StringComparison.Ordinal);
        Assert.Contains("`after:a1b2c3` `after:d4e5f6`", EntryTextParser.WithDependsOn(raw, ["a1b2c3", "d4e5f6"]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeat_is_written_back_in_the_form_a_person_would_have_typed()
    {
        var weekdays = new Recurrence(1, RecurrenceUnit.Week, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);

        Assert.Equal("weekly", EntryTextParser.RepeatToken(new Recurrence(1, RecurrenceUnit.Week)));
        Assert.Equal("weekdays", EntryTextParser.RepeatToken(weekdays));
        Assert.Equal("2w", EntryTextParser.RepeatToken(new Recurrence(2, RecurrenceUnit.Week)));
        Assert.Equal("daily", EntryTextParser.RepeatToken(new Recurrence(1, RecurrenceUnit.Day)));
    }

    [Fact]
    public void An_entry_with_no_meta_line_keeps_its_scheduling_when_another_field_is_written()
    {
        // The no-meta-line branch reconstructs the line from the parse, so a
        // reconstruction that forgets a field deletes it as a side effect of
        // editing an unrelated one.
        const string raw = "Title with no meta line\n\nSome prose.\n";

        var rewritten = EntryTextParser.WithDue(raw, new DateOnly(2026, 8, 21));
        var again = EntryTextParser.WithArea(rewritten, "repos");

        Assert.Contains("`due:2026-08-21`", again, StringComparison.Ordinal);
        Assert.Contains("`@repos`", again, StringComparison.Ordinal);
    }

    // --- Sub-items from level-2 headings ---------------------------------

    [Fact]
    public void A_level_two_heading_becomes_a_sub_item()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task`\n\n## Draft the schema\n");

        var item = Assert.Single(parsed.SubItems);
        Assert.Equal("Draft the schema", item.Title);
        Assert.False(item.Done);
    }

    [Fact]
    public void A_level_two_heading_takes_the_prose_beneath_it_as_notes()
    {
        var parsed = EntryTextParser.Parse(
            "# Title\n\n## Draft the schema\nStart from the existing frontmatter.\nKeep it flat.\n");

        var item = Assert.Single(parsed.SubItems);
        Assert.Equal("Draft the schema", item.Title);
        Assert.Equal("Start from the existing frontmatter.\nKeep it flat.", item.Notes);
    }

    [Fact]
    public void A_level_two_heading_immediately_after_the_title_still_becomes_a_sub_item()
    {
        var parsed = EntryTextParser.Parse("# Title\n## Draft the schema\n");

        Assert.Equal("Title", parsed.Title);
        var item = Assert.Single(parsed.SubItems);
        Assert.Equal("Draft the schema", item.Title);
    }

    [Fact]
    public void A_level_two_heading_can_be_marked_done()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n## [x] Draft the schema\n");

        var item = Assert.Single(parsed.SubItems);
        Assert.Equal("Draft the schema", item.Title);
        Assert.True(item.Done);
    }

    [Fact]
    public void Several_level_two_headings_become_several_sub_items_in_order()
    {
        var parsed = EntryTextParser.Parse(
            "# Title\n\n## First\nnotes one\n\n## Second\nnotes two\n\n## Third\n");

        Assert.Equal(["First", "Second", "Third"], parsed.SubItems.Select(s => s.Title));
        Assert.Equal("notes one", parsed.SubItems[0].Notes);
        Assert.Equal("notes two", parsed.SubItems[1].Notes);
        Assert.Null(parsed.SubItems[2].Notes);
    }

    [Fact]
    public void A_level_three_heading_becomes_a_nested_sub_item()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task` `*medium` `!draft` `@parent`\n\n## Parent\n\n### Child\n`prompt` `*high` `!ready` `@other` `#child`\nNotes\n");

        Assert.Equal(["Parent", "Child"], parsed.SubItems.Select(s => s.Title));
        Assert.Equal(3, parsed.SubItems[1].Level);
        Assert.Equal("parent", parsed.SubItems[1].Area);
        Assert.Equal(EntryStatus.Ready, parsed.SubItems[1].Status);
    }


    [Fact]
    public void Sub_item_text_returns_only_the_clicked_chapter()
    {
        const string raw =
            "# Parent\n" +
            "`task` `*medium` `!draft` `@repo`\n\n" +
            "Intro text.\n\n" +
            "## Level two\n" +
            "`task` `*high` `!ready`\n" +
            "Only level two notes.\n\n" +
            "### Level three\n" +
            "`idea` `*low` `!draft`\n" +
            "Only level three notes.\n\n" +
            "## Sibling\n" +
            "Sibling notes.\n";

        var levelTwo = EntryTextParser.GetSubItemText(raw, 0);
        var levelThree = EntryTextParser.GetSubItemText(raw, 1);

        Assert.StartsWith("## Level two", levelTwo);
        Assert.Contains("Only level two notes.", levelTwo);
        Assert.DoesNotContain("### Level three", levelTwo);
        Assert.StartsWith("### Level three", levelThree);
        Assert.Contains("Only level three notes.", levelThree);
        Assert.DoesNotContain("## Sibling", levelThree);
    }

    [Fact]
    public void Parent_text_stops_before_heading_sub_items()
    {
        const string raw =
            "# Parent\n" +
            "`task` `*medium` `!draft` `@repo`\n\n" +
            "Intro text.\n\n" +
            "## Level two\n" +
            "Child notes.\n\n" +
            "### Level three\n" +
            "Nested notes.\n";

        var parent = EntryTextParser.GetParentText(raw);

        Assert.Contains("Intro text.", parent);
        Assert.DoesNotContain("## Level two", parent);
        Assert.DoesNotContain("### Level three", parent);
    }

    [Fact]
    public void Replacing_parent_text_preserves_child_chapters()
    {
        const string raw =
            "# Parent\n" +
            "`task` `*medium` `!draft` `@repo`\n\n" +
            "Old intro.\n\n" +
            "## Level two\n" +
            "Child notes.\n\n" +
            "### Level three\n" +
            "Nested notes.\n";

        var rewritten = EntryTextParser.ReplaceParentText(raw,
            "# Parent\n`task` `*medium` `!ready` `@repo`\n\nNew intro.");

        Assert.Contains("New intro.", rewritten);
        Assert.DoesNotContain("Old intro.", rewritten);
        Assert.Contains("## Level two\nChild notes.", rewritten);
        Assert.Contains("### Level three\nNested notes.", rewritten);
    }

    /// <summary>
    /// An editor writes back what it is holding on every keystroke, so replacing
    /// the parent text has to land in the same place twice. Told how many
    /// sub-items the entry started with, it does — including once the text being
    /// written in has grown a <c>##</c> heading of its own, which is otherwise
    /// mistaken for the first existing chapter and hands the real one back again.
    /// </summary>
    [Fact]
    public void Replacing_parent_text_is_repeatable_once_it_holds_a_new_sub_item()
    {
        const string raw =
            "# Parent\n" +
            "`task` `*medium` `!draft`\n\n" +
            "## Existing\n" +
            "Child notes.\n";

        var subItems = EntryTextParser.CountSubItems(raw);
        Assert.Equal(1, subItems);

        var editorText = EntryTextParser.GetParentText(raw, subItems) + "\n\n## Added\nNew notes.\n";

        var once = EntryTextParser.ReplaceParentText(raw, editorText, subItems);
        var twice = EntryTextParser.ReplaceParentText(once, editorText, subItems);

        Assert.Equal(once, twice);
        Assert.Equal(2, EntryTextParser.CountSubItems(once));

        // And the editor keeps showing what was written into it, rather than
        // dropping the new heading the moment it is recognised.
        Assert.Contains("## Added", EntryTextParser.GetParentText(once, subItems));
    }

    [Fact]
    public void Replacing_sub_item_text_changes_only_that_chapter()
    {
        const string raw =
            "# Parent\n" +
            "`task` `*medium` `!draft` `@repo`\n\n" +
            "## First\n" +
            "Keep this.\n\n" +
            "### Target\n" +
            "Old target notes.\n\n" +
            "## Last\n" +
            "Keep last.\n";

        var rewritten = EntryTextParser.ReplaceSubItemText(raw, 1, "### Updated target\nNew target notes.");

        Assert.Contains("## First\nKeep this.", rewritten);
        Assert.Contains("### Updated target\nNew target notes.", rewritten);
        Assert.DoesNotContain("Old target notes.", rewritten);
        Assert.Contains("## Last\nKeep last.", rewritten);
        Assert.StartsWith("# Parent", rewritten);
    }

    [Fact]
    public void A_heading_written_without_a_space_is_not_a_sub_item()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n##Nospace\n");

        Assert.Empty(parsed.SubItems);
    }

    // --- Sub-items from checklists ---------------------------------------

    [Fact]
    public void Checklist_lines_become_sub_items()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n- [ ] one\n- [x] two\n");

        Assert.Equal(["one", "two"], parsed.SubItems.Select(s => s.Title));
        Assert.False(parsed.SubItems[0].Done);
        Assert.True(parsed.SubItems[1].Done);
    }

    [Fact]
    public void A_checklist_under_a_heading_stays_its_own_sub_item()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n## Group\nsome notes\n- [ ] nested\n");

        Assert.Equal(["Group", "nested"], parsed.SubItems.Select(s => s.Title));
        Assert.Equal("some notes", parsed.SubItems[0].Notes);
    }

    [Fact]
    public void A_plain_bullet_is_not_a_sub_item()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n- just a bullet\n");

        Assert.Empty(parsed.SubItems);
    }

    [Fact]
    public void Headings_inside_a_fenced_block_are_left_alone()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n```\n## not a sub-item\n```\n");

        Assert.Empty(parsed.SubItems);
    }

    // --- Splitting on a second level-1 heading ---------------------------

    [Fact]
    public void A_single_entry_is_one_segment()
    {
        var segments = EntryTextParser.SplitSegments("# Only one\n\nbody\n");

        Assert.Single(segments);
    }

    [Fact]
    public void A_second_level_one_heading_starts_a_new_entry()
    {
        var segments = EntryTextParser.SplitSegments("# First\nbody one\n\n# Second\nbody two\n");

        Assert.Equal(2, segments.Count);
        Assert.StartsWith("# First", segments[0]);
        Assert.StartsWith("# Second", segments[1]);
    }

    [Fact]
    public void Level_two_headings_never_split_an_entry()
    {
        var segments = EntryTextParser.SplitSegments("# First\n\n## sub one\n\n## sub two\n");

        Assert.Single(segments);
    }

    [Fact]
    public void Parent_status_rewrite_cascades_to_sub_item_chapters()
    {
        const string raw =
            "# Parent\n" +
            "`task` `*medium` `!draft` `@repo`\n\n" +
            "## Child\n" +
            "`task` `*low` `!ready` `@other`\n\n" +
            "### Grandchild\n" +
            "`task` `*high` `!done`\n";

        var rewritten = EntryTextParser.WithStatus(raw, EntryStatus.InProgress, cascadeSubItems: true);

        Assert.Contains("# Parent\n`task` `*medium` `!in-progress` `@repo`", rewritten);
        Assert.Contains("## Child\n`task` `*low` `!in-progress`", rewritten);
        Assert.Contains("### Grandchild\n`task` `*high` `!in-progress`", rewritten);
        Assert.DoesNotContain("`@other`", rewritten);
    }

    [Fact]
    public void Sub_item_status_rewrite_does_not_change_parent_or_siblings()
    {
        const string raw = "# Parent\n`task` `*medium` `!draft` `@repo`\n\n## Child\n`task` `*low` `!ready`\n\n### Grandchild\n`task` `*high` `!done`\n";

        var rewritten = EntryTextParser.WithSubItemStatus(raw, 0, EntryStatus.Archived);

        Assert.Contains("# Parent\n`task` `*medium` `!draft` `@repo`", rewritten);
        Assert.Contains("## Child\n`task` `*low` `!archived`", rewritten);
        Assert.Contains("### Grandchild\n`task` `*high` `!done`", rewritten);
    }

    [Fact]
    public void A_level_one_heading_inside_a_fence_does_not_split()
    {
        var segments = EntryTextParser.SplitSegments("# First\n\n```\n# not a title\n```\n\nmore\n");

        Assert.Single(segments);
    }

    [Fact]
    public void A_title_typed_without_a_hash_still_absorbs_the_first_heading()
    {
        // The first line is the title whether or not it is written as a
        // heading, so a heading on line two is the first real split point.
        var segments = EntryTextParser.SplitSegments("Plain title\n\n# A real heading\n");

        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void A_tag_inside_a_code_fence_is_code_not_a_tag()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n#real\n\n```\n#notatag\n```\n\n#alsoreal");

        Assert.Equal(["real", "alsoreal"], parsed.Tags);
    }

    [Fact]
    public void An_unterminated_fence_swallows_the_tags_after_it()
    {
        // Everything after an unclosed fence is code as far as the writer is
        // concerned; guessing otherwise would tag things they never tagged.
        var parsed = EntryTextParser.Parse("# Title\n\n#real\n\n```\n#notatag");

        Assert.Equal(["real"], parsed.Tags);
    }

    // --- Round-tripping ---------------------------------------------------

    [Fact]
    public void Each_kind_of_metadata_has_its_own_sigil()
    {
        var parsed = EntryTextParser.Parse("# Title\n`idea` `*critical` `!archived` `@side-project`\n");

        Assert.Equal(EntryType.Idea, parsed.Type);
        Assert.Equal(Priority.Critical, parsed.Priority);
        Assert.Equal(EntryStatus.Archived, parsed.Status);
        Assert.Equal("side-project", parsed.Area);
    }

    [Fact]
    public void A_sigil_settles_a_word_that_two_kinds_could_claim()
    {
        // "done" is a status. Sigilled as a priority it is simply not a
        // priority, and must not fall through and be read as a status anyway.
        var parsed = EntryTextParser.Parse("# Title\n`*done`\n");

        Assert.Null(parsed.Priority);
        Assert.Null(parsed.Status);
    }

    [Fact]
    public void A_status_sigil_is_read_as_a_status_and_nothing_else()
    {
        var parsed = EntryTextParser.Parse("# Title\n`!task`\n");

        Assert.Null(parsed.Type);
        Assert.Null(parsed.Status);
    }

    [Theory]
    [InlineData("`!in-progress`", EntryStatus.InProgress)]
    [InlineData("`!In Progress`", EntryStatus.InProgress)]
    [InlineData("`!in_progress`", EntryStatus.InProgress)]
    [InlineData("`!DONE`", EntryStatus.Done)]
    public void A_sigilled_status_is_as_forgiving_about_spelling_as_a_bare_one(string token, EntryStatus expected)
    {
        Assert.Equal(expected, EntryTextParser.Parse($"# Title\n{token}\n").Status);
    }

    [Fact]
    public void Bare_words_written_before_the_sigils_existed_still_read()
    {
        var parsed = EntryTextParser.Parse("# Title\n`idea` `critical` `archived`\n");

        Assert.Equal(EntryType.Idea, parsed.Type);
        Assert.Equal(Priority.Critical, parsed.Priority);
        Assert.Equal(EntryStatus.Archived, parsed.Status);
    }

    [Fact]
    public void The_canonical_form_written_back_uses_sigils()
    {
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task, Priority.High);

        var raw = EntryTextParser.ToRawText(entry.ToDto());

        Assert.Contains("`*high`", raw);
        Assert.Contains("`!draft`", raw);
        Assert.Contains("`task`", raw);
    }

    [Fact]
    public void A_bare_meta_line_is_rewritten_with_sigils_and_still_means_the_same()
    {
        var before = EntryTextParser.Parse("# Ship it\n`idea` `critical` `draft`\n");

        var entry = new TaskItem("Ship it", string.Empty, before.Type!.Value, before.Priority!.Value);
        var after = EntryTextParser.Parse(EntryTextParser.ToRawText(entry.ToDto()));

        Assert.Equal(before.Type, after.Type);
        Assert.Equal(before.Priority, after.Priority);
        Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public void Raw_text_round_trips_through_an_entry()
    {
        var entry = new TaskItem("Ship it", "Body with #alpha\n\n## A sub-item\nnotes", EntryType.Idea, Priority.High);
        entry.SetArea("repos");
        entry.SetTags(["beta"]);

        var raw = EntryTextParser.ToRawText(entry.ToDto());
        var parsed = EntryTextParser.Parse(raw);

        Assert.Equal("Ship it", parsed.Title);
        Assert.Equal(EntryType.Idea, parsed.Type);
        Assert.Equal(Priority.High, parsed.Priority);
        Assert.Equal(EntryStatus.Draft, parsed.Status);
        Assert.Equal("repos", parsed.Area);
        Assert.Equal(["beta", "alpha"], parsed.Tags);
        Assert.Equal("A sub-item", Assert.Single(parsed.SubItems).Title);
    }

    /// <summary>
    /// The canonical rewrite is destructive by design: it composes the metadata
    /// line from the entry model alone, so a field the model carries and this
    /// method forgets is destroyed on the next save with no error to notice it by.
    /// </summary>
    [Fact]
    public void The_canonical_form_carries_the_scheduling_and_dependency_tokens()
    {
        var entry = new TaskItem("Deploy SpecManager", string.Empty, EntryType.Task, Priority.High);
        entry.SetDueOn(new DateOnly(2026, 8, 21));
        entry.SetReminder(new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Unspecified));
        entry.SetRecurrence(new Recurrence(1, RecurrenceUnit.Week));
        entry.SetInMyDayOn(new DateOnly(2026, 8, 19));
        entry.SetDependsOn(["a1b2c3", "d4e5f6"]);

        var raw = EntryTextParser.ToRawText(entry.ToDto());
        var parsed = EntryTextParser.Parse(raw);

        Assert.Equal(
            "`task` `*high` `!draft` `due:2026-08-21` `remind:2026-08-21T09:00` `repeat:weekly` `myday:2026-08-19` `after:a1b2c3` `after:d4e5f6`",
            raw.Split('\n')[1]);
        Assert.Equal(entry.DueOn, parsed.DueOn);
        Assert.Equal(entry.RemindAt, parsed.RemindAt);
        Assert.Equal(entry.Recurrence, parsed.Recurrence);
        Assert.Equal(entry.InMyDayOn, parsed.InMyDayOn);
        Assert.Equal(entry.DependsOn, parsed.DependsOn!);
    }

    // --- Syncing onto the aggregate --------------------------------------

    [Fact]
    public void Syncing_adds_removes_and_renames_sub_items_to_match_the_text()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        TaskTextSync.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## one\n\n## two\n").SubItems);
        Assert.Equal(["one", "two"], entry.SubItems.Select(s => s.Title));

        TaskTextSync.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## renamed\n").SubItems);
        var item = Assert.Single(entry.SubItems);
        Assert.Equal("renamed", item.Title);
    }

    [Fact]
    public void Syncing_carries_the_done_state_across()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        TaskTextSync.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## [x] done one\n- [x] done two\n").SubItems);

        Assert.Equal(2, entry.TotalSubItemCount);
        Assert.Equal(2, entry.CompletedSubItemCount);
    }

    [Fact]
    public void Syncing_carries_notes_across()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        TaskTextSync.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## one\nsome notes\n").SubItems);

        Assert.Equal("some notes", Assert.Single(entry.SubItems).Notes);
    }

    // --- Effort ------------------------------------------------------------
    //
    // The size token is `effort:<n>`. Parsing is as tolerant as every other value
    // on the line: a value that is not a non-negative whole number leaves the field
    // unset rather than failing the line around it.

    [Fact]
    public void An_effort_token_parses_to_a_point_count()
    {
        var parsed = EntryTextParser.Parse("# Size me\n`task` `effort:5`\n");

        Assert.Equal(5, parsed.Effort);
    }

    [Fact]
    public void Zero_effort_parses_and_is_not_null()
    {
        var parsed = EntryTextParser.Parse("# Size me\n`task` `effort:0`\n");

        Assert.Equal(0, parsed.Effort);
    }

    [Fact]
    public void A_missing_effort_token_leaves_the_size_unset()
    {
        var parsed = EntryTextParser.Parse("# Size me\n`task` `*high`\n");

        Assert.Null(parsed.Effort);
    }

    [Theory]
    [InlineData("effort:abc")]
    [InlineData("effort:-2")]
    [InlineData("effort:")]
    [InlineData("effort:3.5")]
    public void An_unreadable_effort_leaves_the_size_unset_without_throwing(string token)
    {
        var parsed = EntryTextParser.Parse($"# Size me\n`task` `{token}`\n");

        Assert.Null(parsed.Effort);
    }

    [Fact]
    public void The_canonical_form_carries_the_effort_token_after_the_dependencies()
    {
        var entry = new TaskItem("Deploy SpecManager", string.Empty, EntryType.Task, Priority.High);
        entry.SetDueOn(new DateOnly(2026, 8, 21));
        entry.SetDependsOn(["a1b2c3"]);
        entry.SetEffort(8);

        var raw = EntryTextParser.ToRawText(entry.ToDto());
        var parsed = EntryTextParser.Parse(raw);

        Assert.Equal(
            "`task` `*high` `!draft` `due:2026-08-21` `after:a1b2c3` `effort:8`",
            raw.Split('\n')[1]);
        Assert.Equal(8, parsed.Effort);
    }

    [Fact]
    public void Effort_round_trips_through_a_parse_write_parse()
    {
        var once = EntryTextParser.Parse("# Size me\n`task` `*medium` `!ready` `effort:13`\n\nBody.\n");

        var entry = new TaskItem("Size me", once.Body, once.Type!.Value, once.Priority!.Value);
        entry.SetEffort(once.Effort);
        var raw = EntryTextParser.ToRawText(entry.ToDto());

        var twice = EntryTextParser.Parse(raw);

        Assert.Equal(13, once.Effort);
        Assert.Equal(once.Effort, twice.Effort);
        // Writing the same text again lands the same line.
        Assert.Equal(raw, EntryTextParser.ToRawText(entry.ToDto()));
    }

    [Fact]
    public void WithEffort_adds_the_token_when_there_is_none()
    {
        var raw = "# Size me\n`task` `*high` `!ready`\n";

        var rewritten = EntryTextParser.WithEffort(raw, 5);

        Assert.Contains("`effort:5`", rewritten, StringComparison.Ordinal);
        Assert.Equal(5, EntryTextParser.Parse(rewritten).Effort);
        // On the metadata line beside the other tokens, not before the sigils.
        var metaLine = rewritten.Split('\n')[1];
        Assert.EndsWith("`effort:5`", metaLine, StringComparison.Ordinal);
        Assert.StartsWith("`task`", metaLine, StringComparison.Ordinal);
    }

    [Fact]
    public void WithEffort_replaces_an_existing_token_rather_than_doubling_it()
    {
        var raw = "# Size me\n`task` `effort:3`\n";

        var rewritten = EntryTextParser.WithEffort(raw, 8);

        Assert.Contains("`effort:8`", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("effort:3", rewritten, StringComparison.Ordinal);
        Assert.Equal(8, EntryTextParser.Parse(rewritten).Effort);
    }

    [Fact]
    public void WithEffort_null_removes_the_token()
    {
        var raw = "# Size me\n`task` `effort:8`\n";

        var rewritten = EntryTextParser.WithEffort(raw, null);

        Assert.DoesNotContain("effort:", rewritten, StringComparison.Ordinal);
        Assert.Null(EntryTextParser.Parse(rewritten).Effort);
    }
}

