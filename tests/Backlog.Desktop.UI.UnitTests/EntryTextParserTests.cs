using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

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
    public void A_level_three_heading_is_prose_not_a_sub_item()
    {
        var parsed = EntryTextParser.Parse("# Title\n\n### Just a heading\n");

        Assert.Empty(parsed.SubItems);
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
        var entry = new BacklogEntry("Ship it", string.Empty, EntryType.Task, Priority.High);

        var raw = EntryTextParser.ToRawText(entry);

        Assert.Contains("`*high`", raw);
        Assert.Contains("`!draft`", raw);
        Assert.Contains("`task`", raw);
    }

    [Fact]
    public void A_bare_meta_line_is_rewritten_with_sigils_and_still_means_the_same()
    {
        var before = EntryTextParser.Parse("# Ship it\n`idea` `critical` `draft`\n");

        var entry = new BacklogEntry("Ship it", string.Empty, before.Type!.Value, before.Priority!.Value);
        var after = EntryTextParser.Parse(EntryTextParser.ToRawText(entry));

        Assert.Equal(before.Type, after.Type);
        Assert.Equal(before.Priority, after.Priority);
        Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public void Raw_text_round_trips_through_an_entry()
    {
        var entry = new BacklogEntry("Ship it", "Body with #alpha\n\n## A sub-item\nnotes", EntryType.Idea, Priority.High);
        entry.SetArea("repos");

        var raw = EntryTextParser.ToRawText(entry);
        var parsed = EntryTextParser.Parse(raw);

        Assert.Equal("Ship it", parsed.Title);
        Assert.Equal(EntryType.Idea, parsed.Type);
        Assert.Equal(Priority.High, parsed.Priority);
        Assert.Equal(EntryStatus.Draft, parsed.Status);
        Assert.Equal("repos", parsed.Area);
        Assert.Equal(["alpha"], parsed.Tags);
        Assert.Equal("A sub-item", Assert.Single(parsed.SubItems).Title);
    }

    // --- Syncing onto the aggregate --------------------------------------

    [Fact]
    public void Syncing_adds_removes_and_renames_sub_items_to_match_the_text()
    {
        var entry = new BacklogEntry("Title", string.Empty, EntryType.Task);

        EntryTextParser.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## one\n\n## two\n").SubItems);
        Assert.Equal(["one", "two"], entry.SubItems.Select(s => s.Title));

        EntryTextParser.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## renamed\n").SubItems);
        var item = Assert.Single(entry.SubItems);
        Assert.Equal("renamed", item.Title);
    }

    [Fact]
    public void Syncing_carries_the_done_state_across()
    {
        var entry = new BacklogEntry("Title", string.Empty, EntryType.Task);

        EntryTextParser.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## [x] done one\n- [x] done two\n").SubItems);

        Assert.Equal(2, entry.TotalSubItemCount);
        Assert.Equal(2, entry.CompletedSubItemCount);
    }

    [Fact]
    public void Syncing_carries_notes_across()
    {
        var entry = new BacklogEntry("Title", string.Empty, EntryType.Task);

        EntryTextParser.SyncSubItems(entry, EntryTextParser.Parse("# Title\n\n## one\nsome notes\n").SubItems);

        Assert.Equal("some notes", Assert.Single(entry.SubItems).Notes);
    }
}
