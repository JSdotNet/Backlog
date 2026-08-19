namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The alignment algorithm, on its own. It is the only part of the section
/// comparison that is pure logic, and it is where every judgement call lives —
/// what counts as a rename, what a level change means, which of two duplicate
/// headings pairs with which — so it is tested here rather than through the
/// component that draws it.
/// </summary>
public sealed class MarkdownCompareTests
{
    [Fact]
    public void Identical_documents_report_every_block_unchanged()
    {
        const string Document = """
            # Release notes

            The headline paragraph.

            ## Installing the CLI

            - Download the archive
            - Unpack it

            ```bash
            backlog --version
            ```

            | Platform | Status |
            | --- | --- |
            | Windows | Supported |
            """;

        var root = MarkdownCompare.Compare(Document, Document);

        Assert.True(root.IsWhollyUnchanged);
        Assert.All(EveryBlock(root), block => Assert.Equal(ChangeKind.Unchanged, block.Kind));
        Assert.All(EverySection(root), section => Assert.Equal(ChangeKind.Unchanged, section.Kind));

        // The guard against a vacuous pass: a parser that handed back nothing
        // would satisfy every assertion above.
        // A paragraph, a list, a fence and a table — every block shape the
        // fingerprint has an arm for.
        Assert.Equal(4, EveryBlock(root).Count);
    }

    [Fact]
    public void A_renamed_heading_is_one_changed_section_and_its_body_stays_unchanged()
    {
        const string Before = """
            # Release notes

            ## Setup

            Install the CLI from the release page.

            Then run it once to write a config.
            """;

        const string After = """
            # Release notes

            ## Installation

            Install the CLI from the release page.

            Then run it once to write a config.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);
        var section = Only(notes.Children);

        // One changed row, and below it only what really moved — which is
        // nothing. Keyed on heading text alone this would be a whole section
        // removed and a whole section added, reporting both untouched
        // paragraphs twice.
        Assert.Equal(ChangeKind.Changed, section.Kind);
        Assert.Equal("Setup", section.BeforeHeading);
        Assert.Equal("Installation", section.AfterHeading);
        Assert.All(section.Blocks, block => Assert.Equal(ChangeKind.Unchanged, block.Kind));
        Assert.Equal(2, section.Blocks.Count);
    }

    [Fact]
    public void A_renamed_heading_with_a_rewritten_body_is_still_one_changed_section()
    {
        // Nothing of the body survives, so the pairing rests entirely on the
        // heading texts being similar — either half of the rule is enough.
        const string Before = """
            # Release notes

            ## Setup

            Install the CLI from the release page.
            """;

        const string After = """
            # Release notes

            ## Setup guide

            Pull the container image and run it.
            """;

        var section = Only(Only(MarkdownCompare.Compare(Before, After).Children).Children);

        Assert.Equal(ChangeKind.Changed, section.Kind);
        Assert.Equal("Setup", section.BeforeHeading);
        Assert.Equal("Setup guide", section.AfterHeading);
        Assert.Equal(ChangeKind.Changed, Only(section.Blocks).Kind);
    }

    [Fact]
    public void An_unrelated_heading_with_an_unrelated_body_is_a_removal_and_an_addition()
    {
        const string Before = """
            # Release notes

            ## Setup

            Install the CLI from the release page.
            """;

        const string After = """
            # Release notes

            ## Roadmap

            Quarterly plan for the next two releases.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);

        Assert.Equal(
            [ChangeKind.Removed, ChangeKind.Added],
            notes.Children.Select(child => child.Kind));

        Assert.Equal("Setup", notes.Children[0].BeforeHeading);
        Assert.Null(notes.Children[0].AfterHeading);
        Assert.Equal("Roadmap", notes.Children[1].AfterHeading);
        Assert.Null(notes.Children[1].BeforeHeading);
    }

    [Fact]
    public void A_heading_that_changed_level_is_a_removal_and_an_addition()
    {
        // A level change moves the section in the outline and changes the
        // heading path of every descendant, so "changed" would leave the
        // subtree aligned across two different paths.
        const string Before = """
            # Release notes

            ## Setup

            Install the CLI from the release page.
            """;

        const string After = """
            # Release notes

            ### Setup

            Install the CLI from the release page.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);

        Assert.Equal(
            [ChangeKind.Removed, ChangeKind.Added],
            notes.Children.Select(child => child.Kind));

        Assert.Equal(2, notes.Children[0].Level);
        Assert.Equal(3, notes.Children[1].Level);
    }

    [Fact]
    public void Duplicate_heading_texts_pair_by_position()
    {
        const string Before = """
            # Release notes

            ## Notes

            The first note.

            ## Notes

            The second note.
            """;

        const string After = """
            # Release notes

            ## Notes

            The first note, edited.

            ## Notes

            The second note.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);

        Assert.Equal(2, notes.Children.Count);

        // Position is the only tiebreaker, and it is stable: the first pairs
        // with the first even though the second is the one that still matches.
        Assert.Equal(ChangeKind.Changed, Only(notes.Children[0].Blocks).Kind);
        Assert.True(notes.Children[1].IsWhollyUnchanged);
    }

    [Fact]
    public void A_third_duplicate_with_no_counterpart_falls_through_to_removal()
    {
        const string Before = """
            # Release notes

            ## Notes

            One.

            ## Notes

            Two.

            ## Notes

            Three.
            """;

        const string After = """
            # Release notes

            ## Notes

            One.

            ## Notes

            Two.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);

        Assert.Equal(
            [ChangeKind.Unchanged, ChangeKind.Unchanged, ChangeKind.Removed],
            notes.Children.Select(child => child.Kind));
    }

    [Fact]
    public void A_rename_under_one_parent_does_not_match_a_heading_under_another()
    {
        const string Before = """
            # Release notes

            ## Chapter one

            ### Setup

            Install the CLI from the release page.

            ## Chapter two
            """;

        const string After = """
            # Release notes

            ## Chapter one

            ## Chapter two

            ### Setup guide

            Install the CLI from the release page.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);
        var one = notes.Children.Single(child => child.Heading == "Chapter one");
        var two = notes.Children.Single(child => child.Heading == "Chapter two");

        // Matching is scoped to siblings under an already-matched parent, so a
        // rename in one chapter cannot steal a heading from another.
        Assert.Equal(ChangeKind.Removed, Only(one.Children).Kind);
        Assert.Equal(ChangeKind.Added, Only(two.Children).Kind);
    }

    [Fact]
    public void A_deleted_section_reports_its_whole_subtree_as_one_removal()
    {
        const string Before = """
            # Release notes

            ## Setup

            Install the CLI.

            ### Windows

            Use the MSI.

            ### Linux

            Use the tarball.

            ## Other

            Other prose.
            """;

        const string After = """
            # Release notes

            ## Other

            Other prose.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);

        // Two rows, not four: the deleted section carries its descendants
        // rather than each of them being reported beside it.
        Assert.Equal(2, notes.Children.Count);

        var removed = notes.Children[0];
        Assert.Equal(ChangeKind.Removed, removed.Kind);
        Assert.Equal("Setup", removed.BeforeHeading);
        Assert.Equal(["Windows", "Linux"], removed.Children.Select(child => child.BeforeHeading));
        Assert.All(removed.Children, child => Assert.Equal(ChangeKind.Removed, child.Kind));
        Assert.All(EveryBlock(removed), block => Assert.Equal(ChangeKind.Removed, block.Kind));

        Assert.True(notes.Children[1].IsWhollyUnchanged);
    }

    [Fact]
    public void Two_empty_headings_in_the_same_slot_read_as_a_rename()
    {
        // The accepted consequence of "both bodies empty means identical
        // bodies". Nothing is hidden by it — both heading texts stay on screen
        // — so it is a labelling difference and not lost information.
        var section = Only(Only(MarkdownCompare.Compare(
            "# Release notes\n\n## Alpha\n",
            "# Release notes\n\n## Zulu\n").Children).Children);

        Assert.Equal(ChangeKind.Changed, section.Kind);
        Assert.Equal("Alpha", section.BeforeHeading);
        Assert.Equal("Zulu", section.AfterHeading);
    }

    [Fact]
    public void A_case_change_in_a_heading_is_a_change_not_a_match()
    {
        const string Before = """
            # Release notes

            ## Setup

            Install the CLI from the release page.
            """;

        const string After = """
            # Release notes

            ## SETUP

            Install the CLI from the release page.
            """;

        var section = Only(Only(MarkdownCompare.Compare(Before, After).Children).Children);

        // Exact matching is ordinal and case-sensitive, so this is a real edit
        // and shows as one; the similarity pass then pairs it as a rename
        // rather than splitting it into a removal and an addition.
        Assert.Equal(ChangeKind.Changed, section.Kind);
        Assert.Equal("Setup", section.BeforeHeading);
        Assert.Equal("SETUP", section.AfterHeading);
        Assert.All(section.Blocks, block => Assert.Equal(ChangeKind.Unchanged, block.Kind));
    }

    [Fact]
    public void An_edited_paragraph_is_one_changed_block_a_replaced_table_is_a_removal_and_an_addition()
    {
        const string Before = """
            # Release notes

            The first paragraph.

            | Platform | Status |
            | --- | --- |
            | Windows | Supported |

            The last paragraph.
            """;

        const string After = """
            # Release notes

            The first paragraph, edited.

            Prose where the table used to be.

            The last paragraph.
            """;

        var notes = Only(MarkdownCompare.Compare(Before, After).Children);

        // Requiring the same block type is what stops a deleted table and an
        // inserted paragraph being reported as one edited block.
        Assert.Equal(
            [ChangeKind.Changed, ChangeKind.Removed, ChangeKind.Added, ChangeKind.Unchanged],
            notes.Blocks.Select(block => block.Kind));
    }

    [Fact]
    public void IsWhollyUnchanged_is_false_when_only_a_grandchild_changed()
    {
        const string Before = """
            # Release notes

            ## Setup

            ### Windows

            The old note.

            ## Other

            Other prose.
            """;

        const string After = """
            # Release notes

            ## Setup

            ### Windows

            The new note.

            ## Other

            Other prose.
            """;

        var root = MarkdownCompare.Compare(Before, After);
        var notes = Only(root.Children);
        var setup = notes.Children.Single(child => child.Heading == "Setup");

        // The collapse rule is written against this one predicate, so a changed
        // grandchild has to make every ancestor report false — otherwise a
        // section would fold away with a change inside it.
        Assert.False(root.IsWhollyUnchanged);
        Assert.False(notes.IsWhollyUnchanged);
        Assert.False(setup.IsWhollyUnchanged);
        Assert.False(Only(setup.Children).IsWhollyUnchanged);

        // And the sibling that really did not move still says so.
        Assert.True(notes.Children.Single(child => child.Heading == "Other").IsWhollyUnchanged);
    }

    private static T Only<T>(IReadOnlyList<T> items)
    {
        Assert.Single(items);
        return items[0];
    }

    private static IReadOnlyList<ComparedBlock> EveryBlock(ComparedSection section) =>
        [.. EverySection(section).SelectMany(part => part.Blocks)];

    private static IReadOnlyList<ComparedSection> EverySection(ComparedSection section) =>
        [section, .. section.Children.SelectMany(EverySection)];
}
