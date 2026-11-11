using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// A context — where or with what a task can be done — is a tag whose name starts
/// with <c>@</c>, written <c>#@pc</c> (local ADR 0054 §5,
/// <c>.devbook/domain/tasks/domain.md#context</c>). It parses as the tag
/// <c>@pc</c> and survives the canonical rewrite (ADR 0054, verification 6).
/// </summary>
public class EntryContextTagTests
{
    [Fact]
    public void A_context_in_the_body_parses_as_the_tag_at_pc()
    {
        var parsed = EntryTextParser.Parse("# Print the forms\n`task` `!ready`\n\nNeeds the printer #@pc and #paper.\n");

        Assert.Contains("@pc", parsed.Tags);
        Assert.Contains("paper", parsed.Tags);
    }

    [Fact]
    public void A_context_on_the_metadata_line_is_a_tag_and_not_an_area()
    {
        var parsed = EntryTextParser.Parse("# Call the plumber\n`task` `!ready` `@home` `#@calls`\n");

        Assert.Equal("home", parsed.Area);
        Assert.Contains("@calls", parsed.Tags);
        Assert.Contains("@calls", parsed.MetadataTags);
    }

    [Fact]
    public void A_context_on_the_title_is_the_same_tag()
    {
        var parsed = EntryTextParser.Parse("# Call the plumber #@Calls\n`task`\n");

        Assert.Contains("@calls", parsed.Tags);
    }

    /// <summary>An address is still nobody's tag: the guard on the left holds for the
    /// new spelling as it does for the old.</summary>
    [Fact]
    public void A_hash_welded_to_a_word_is_still_no_tag()
    {
        var parsed = EntryTextParser.Parse("# Title\n`task`\n\nSee issue#@pc and mail bob@example.com.\n");

        Assert.DoesNotContain(parsed.Tags, tag => tag.StartsWith('@'));
    }

    [Fact]
    public void A_context_on_the_metadata_line_survives_the_canonical_rewrite()
    {
        var parsed = EntryTextParser.Parse("# Call the plumber\n`task` `!ready` `#@calls`\n");
        var entry = new TaskItem("Call the plumber", string.Empty, EntryType.Task, Priority.Medium);
        TaskEntryFields.ApplyToExisting(entry, parsed);

        var rewritten = EntryTextParser.ToRawText(entry.ToDto());

        Assert.Contains("`#@calls`", rewritten, StringComparison.Ordinal);
        Assert.Contains("@calls", EntryTextParser.Parse(rewritten).Tags);
        Assert.Equal(rewritten, EntryTextParser.ToRawText(ReparsedDto(rewritten)));
    }

    [Fact]
    public void A_context_in_the_body_survives_the_canonical_rewrite_and_stays_prose()
    {
        var parsed = EntryTextParser.Parse("# Print the forms\n`task`\n\nAt the desk #@pc.\n");
        var entry = new TaskItem("Print the forms", "At the desk #@pc.", EntryType.Task, Priority.Medium);
        TaskEntryFields.ApplyToExisting(entry, parsed);

        var rewritten = EntryTextParser.ToRawText(entry.ToDto());

        Assert.DoesNotContain("`#@pc`", rewritten, StringComparison.Ordinal);
        Assert.Contains("At the desk #@pc.", rewritten, StringComparison.Ordinal);
        Assert.Contains("@pc", EntryTextParser.Parse(rewritten).Tags);
    }

    /// <summary>A person on the title is not written on to the metadata line: the
    /// title already carries it.</summary>
    [Fact]
    public void A_person_on_the_title_is_not_copied_on_to_the_metadata_line()
    {
        var parsed = EntryTextParser.Parse("# Ask @carol about the roof\n`task`\n");
        var entry = new TaskItem("Ask @carol about the roof", string.Empty, EntryType.Task, Priority.Medium);
        TaskEntryFields.ApplyToExisting(entry, parsed);

        var rewritten = EntryTextParser.ToRawText(entry.ToDto());

        Assert.DoesNotContain("#@carol", rewritten, StringComparison.Ordinal);
        Assert.Contains("@carol", EntryTextParser.Parse(rewritten).Tags);
    }

    /// <summary>The tag editor hands back the title's <c>@</c> tags; one written
    /// <c>#@calls</c> is carried already, so keeping it adds no second spelling and
    /// dropping it takes it off.</summary>
    [Fact]
    public void The_tag_writer_treats_a_title_context_as_carried()
    {
        const string raw = "# Call the plumber #@calls\n`task`\n";

        Assert.Equal(raw, EntryTextParser.WithPersonTags(raw, ["@calls"]));
        Assert.DoesNotContain("calls", EntryTextParser.WithPersonTags(raw, []).Split('\n')[0], StringComparison.Ordinal);
    }

    /// <summary>Editing another tag leaves a metadata-line context where it is: the
    /// tag writer keeps `#@calls` on the line rather than moving it to the title.</summary>
    [Fact]
    public void A_tag_edit_keeps_a_metadata_line_context_on_the_line()
    {
        const string raw = "# Call the plumber\n`task` `*medium` `!ready` `#@calls`\n";

        var rewritten = EntryTextParser.WithTags(raw, ["@calls", "q4"]);

        Assert.StartsWith("# Call the plumber\n", rewritten, StringComparison.Ordinal);
        Assert.Contains("`#@calls`", rewritten, StringComparison.Ordinal);
        Assert.Contains("`#q4`", rewritten, StringComparison.Ordinal);

        var dropped = EntryTextParser.WithTags(raw, ["q4"]);

        Assert.DoesNotContain("@calls", dropped, StringComparison.Ordinal);
    }

    /// <summary>A person the editor adds still goes on the title, where it always
    /// went.</summary>
    [Fact]
    public void A_person_added_through_the_tag_writer_still_goes_on_the_title()
    {
        var rewritten = EntryTextParser.WithTags("# Fix the roof\n`task`\n", ["@bob"]);

        Assert.StartsWith("# Fix the roof @bob\n", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("#@bob", rewritten, StringComparison.Ordinal);
    }

    private static Backlog.Modules.Tasks.Abstractions.DataTransferObjects.TaskItemDto ReparsedDto(string raw)
    {
        var parsed = EntryTextParser.Parse(raw);
        var entry = new TaskItem(parsed.Title, string.Empty, EntryType.Task, Priority.Medium);
        TaskEntryFields.ApplyToExisting(entry, parsed);
        return entry.ToDto();
    }
}
