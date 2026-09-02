using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The files in <c>samples/entries</c> are documentation, and documentation
/// that nothing checks is just a rumour. These tests parse the real files, so a
/// sample that stops matching the parser fails the build rather than quietly
/// misleading whoever reads it next.
/// </summary>
public sealed class SampleEntryTests
{
    private static readonly string SamplesDirectory = RepositoryRoot.Directory("samples", "entries");

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(SamplesDirectory, name));

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(SamplesDirectory, "*.md"))
        {
            var name = Path.GetFileName(path);
            if (!string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase))
            {
                data.Add(name);
            }
        }

        return data;
    }

    [Fact]
    public void The_samples_folder_is_where_the_readme_says_it_is()
    {
        Assert.True(File.Exists(Path.Combine(SamplesDirectory, "README.md")));
        Assert.NotEmpty(SampleFiles());
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_sample_parses_into_something_with_a_title(string file)
    {
        var parsed = EntryTextParser.Parse(Read(file));

        Assert.False(string.IsNullOrWhiteSpace(parsed.Title));
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_sample_is_one_entry_not_several(string file)
    {
        // A stray second `# ` heading would silently split the sample in two
        // when someone pasted it in.
        Assert.Single(EntryTextParser.SplitSegments(Read(file)));
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_sample_renders_without_losing_its_body(string file)
    {
        var parsed = EntryTextParser.Parse(Read(file));

        if (parsed.Body.Trim().Length > 0)
        {
            Assert.NotEmpty(MarkdownPreview.Parse(parsed.Body));
        }
    }

    [Fact]
    public void The_minimal_sample_is_a_title_and_nothing_else()
    {
        var parsed = EntryTextParser.Parse(Read("minimal.md"));

        Assert.Equal("Buy a domain for the reading list app", parsed.Title);
        Assert.Null(parsed.Type);
        Assert.Null(parsed.Status);
        Assert.Empty(parsed.SubItems);
        Assert.Empty(parsed.Tags);
    }

    [Fact]
    public void The_full_sample_reads_every_kind_of_metadata()
    {
        var parsed = EntryTextParser.Parse(Read("full.md"));

        Assert.Equal("Ship the offline sync spike", parsed.Title);
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Equal(Priority.High, parsed.Priority);
        Assert.Equal(EntryStatus.InProgress, parsed.Status);
        Assert.Equal("repos", parsed.Area);
        Assert.Equal(["sync", "offline"], parsed.Tags);
    }

    [Fact]
    public void The_full_sample_keeps_its_sub_items_in_the_order_written()
    {
        var parsed = EntryTextParser.Parse(Read("full.md"));

        Assert.Equal(
            [
                "Decide the conflict rule",
                "Measure how often it actually happens",
                "Write the merge test corpus",
                "Read how Obsidian handles this"
            ],
            parsed.SubItems.Select(s => s.Title));

        Assert.Equal([false, true, false, true], parsed.SubItems.Select(s => s.Done));
    }

    /// <summary>
    /// The scheduling sample is documentation for a grammar, so the grammar is
    /// what it is checked against: every named token in it has to read back as the
    /// field it claims to carry, or the file is teaching a syntax the app does not
    /// have.
    /// </summary>
    [Fact]
    public void The_scheduled_sample_reads_every_named_token()
    {
        var parsed = EntryTextParser.Parse(Read("scheduled.md"));

        Assert.Equal("Deploy SpecManager", parsed.Title);
        Assert.Equal(new DateOnly(2026, 8, 21), parsed.DueOn);
        Assert.Equal(new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Unspecified), parsed.RemindAt);
        Assert.Equal(new Recurrence(1, RecurrenceUnit.Week), parsed.Recurrence);
        Assert.Equal(new DateOnly(2026, 8, 19), parsed.InMyDayOn);
        Assert.Equal(["a1b2c3"], parsed.DependsOn);
        Assert.Equal("repos", parsed.Area);
    }

    [Fact]
    public void A_heading_sub_item_keeps_the_notes_written_under_it()
    {
        var parsed = EntryTextParser.Parse(Read("sub-items.md"));

        var first = parsed.SubItems[0];
        Assert.Equal("Cut it to three sentences", first.Title);
        Assert.NotNull(first.Notes);
        Assert.Contains("Nobody reads the fourth", first.Notes);
    }

    [Fact]
    public void A_heading_sub_item_renders_as_a_sub_item_not_a_heading()
    {
        var parsed = EntryTextParser.Parse(Read("sub-items.md"));
        var blocks = MarkdownPreview.Parse(parsed.Body);

        var subItems = blocks.OfType<MdSubItem>().ToList();
        Assert.Equal(2, subItems.Count);
        Assert.All(subItems, s => Assert.NotEmpty(s.Children));
        Assert.DoesNotContain(blocks, b => b is MdHeading { Level: 2 });
    }

    [Fact]
    public void The_checklist_sample_counts_what_is_done()
    {
        var parsed = EntryTextParser.Parse(Read("checklist.md"));

        Assert.Equal(4, parsed.SubItems.Count);
        Assert.Equal(2, parsed.SubItems.Count(s => s.Done));
    }

    [Fact]
    public void The_checklist_sample_renders_as_checkboxes()
    {
        var parsed = EntryTextParser.Parse(Read("checklist.md"));
        var blocks = MarkdownPreview.Parse(parsed.Body);

        var list = Assert.IsType<MdList>(Assert.Single(blocks));
        Assert.All(list.Items, i => Assert.NotNull(i.Done));
    }

    [Fact]
    public void Bare_metadata_words_are_still_read()
    {
        var parsed = EntryTextParser.Parse(Read("bare-words.md"));

        Assert.Equal(EntryType.Idea, parsed.Type);
        Assert.Equal(Priority.Critical, parsed.Priority);
        Assert.Equal(EntryStatus.Archived, parsed.Status);
    }

    [Fact]
    public void Structure_inside_a_code_fence_is_left_alone()
    {
        var parsed = EntryTextParser.Parse(Read("code-fence.md"));

        Assert.Empty(parsed.SubItems);
        Assert.Equal(["docs"], parsed.Tags);
        Assert.Single(EntryTextParser.SplitSegments(Read("code-fence.md")));
    }

    [Fact]
    public void An_entry_with_no_metadata_line_takes_its_first_line_as_the_title()
    {
        var raw = Read("prose-only.md");
        var parsed = EntryTextParser.Parse(raw);

        Assert.Equal("Ask whether the trial length should be 14 days or 30", parsed.Title);
        Assert.Null(parsed.Type);
        Assert.Null(parsed.Priority);
        Assert.Null(parsed.Status);
        Assert.Contains("retention curve", parsed.Body);
    }

    [Fact]
    public void A_sample_written_without_a_heading_gains_one_when_it_is_saved()
    {
        var raw = Read("prose-only.md");

        var normalized = TasksDesktopState.EnsureTitleHeading(raw);

        Assert.StartsWith("# Ask whether the trial length", normalized);
        Assert.Equal(
            EntryTextParser.Parse(raw).Title,
            EntryTextParser.Parse(normalized).Title);
    }
}
