using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The reading order the folder convention derives from names alone — the
/// devbook generator's <c>outline.mjs</c>, which the database builder layers the
/// <c>meta</c>-driven rules on and which the rail and the panes use as it is.
///
/// <para>Every case is names in, names out. Nothing is authored per repository
/// (local ADR 0016); that a stray <c>_reading-order.json</c> changes nothing is
/// asserted where a reader could have read one — the panels, the rail and the
/// builder.</para>
/// </summary>
public sealed class DevbookReadingConventionTests
{
    [Fact]
    public void The_domain_folder_reads_the_context_map_first_and_then_its_contexts_by_name()
    {
        var order = DevbookReadingConvention.Order("domain", 0, ["inbox", "context-map.md", "capture", "tasks"]);

        Assert.Equal(["context-map.md", "capture", "inbox", "tasks"], order);
    }

    [Fact]
    public void A_bounded_context_reads_its_root_then_the_prescribed_files_then_the_rest()
    {
        var order = DevbookReadingConvention.Order(
            "domain",
            1,
            ["flow.md", "model.md", "notes.md", "context.md", "features.md", "domain.md", "actors.md", "skills.md", "dependencies.md"]);

        Assert.Equal(
            ["context.md", "domain.md", "actors.md", "skills.md", "features.md", "model.md", "flow.md", "dependencies.md", "notes.md"],
            order);
    }

    [Fact]
    public void A_split_file_follows_its_base_and_an_invariants_page_follows_the_page_it_belongs_to()
    {
        var order = DevbookReadingConvention.Order(
            "domain",
            1,
            ["domain.order.invariants.md", "features.checkout.md", "domain.invariants.md", "context.md", "model.md", "domain.order.md", "features.md", "domain.md"]);

        Assert.Equal(
            ["context.md", "domain.md", "domain.invariants.md", "domain.order.md", "domain.order.invariants.md", "features.md", "features.checkout.md", "model.md"],
            order);
    }

    [Fact]
    public void A_split_file_takes_its_base_s_place_when_the_base_is_gone()
    {
        var order = DevbookReadingConvention.Order("domain", 1, ["notes.md", "features.checkout.md", "context.md", "actors.md"]);

        Assert.Equal(["context.md", "actors.md", "features.checkout.md", "notes.md"], order);
    }

    [Fact]
    public void The_technology_folder_pins_shared_first_and_tooling_last()
    {
        var order = DevbookReadingConvention.Order("tech", 0, ["tooling.md", "desktop.md", "technology-graph.md", "shared.md", "cloud.md"]);

        Assert.Equal(["technology-graph.md", "shared.md", "cloud.md", "desktop.md", "tooling.md"], order);
    }

    [Fact]
    public void The_design_folder_reads_its_prescribed_guidelines_in_the_convention_s_sequence()
    {
        var order = DevbookReadingConvention.Order(
            "design",
            0,
            ["accessibility.md", "content-editing.md", "color-scheme.md", "README.md", "component-libraries.md", "typography-and-layout.md", "interaction-guidelines.md", "design-principles.md"]);

        Assert.Equal(
            ["README.md", "design-principles.md", "color-scheme.md", "typography-and-layout.md", "interaction-guidelines.md", "accessibility.md", "component-libraries.md", "content-editing.md"],
            order);
    }

    [Fact]
    public void A_numbered_directory_sorts_by_number_and_then_the_unnumbered_by_ordinal_name()
    {
        // arc42 has no convention entry, so it has no root by name either; the
        // record folders and the README follow the chapters, ordinally — which
        // puts an upper-case name ahead of a lower-case one, as JavaScript's
        // default sort does.
        var order = DevbookReadingConvention.Order("arc42", 0, ["tdr", "10-quality.md", "README.md", "02-constraints.md", "adr", "01-introduction.md"]);

        Assert.Equal(["01-introduction.md", "02-constraints.md", "10-quality.md", "README.md", "adr", "tdr"], order);
    }

    [Fact]
    public void A_numbered_folder_with_a_convention_keeps_its_root_ahead_of_the_numbers()
    {
        var order = DevbookReadingConvention.Order("ai", 0, ["concepts.md", "02-code.md", "adoption-map.md", "01-plan.md"]);

        Assert.Equal(["adoption-map.md", "01-plan.md", "02-code.md", "concepts.md"], order);
    }

    [Fact]
    public void A_number_is_compared_as_a_number_not_as_text()
    {
        var order = DevbookReadingConvention.Order("arc42", 1, ["0010-later.md", "0009-earlier.md", "ADR-0002-labelled.md"]);

        Assert.Equal(["ADR-0002-labelled.md", "0009-earlier.md", "0010-later.md"], order);
    }

    [Fact]
    public void A_directory_without_a_convention_sorts_by_ordinal_name()
    {
        Assert.Equal(["B.md", "a.md", "c.md"], DevbookReadingConvention.Order("arc42", 0, ["c.md", "a.md", "B.md"]));
        Assert.Equal(["a.md", "b.md"], DevbookReadingConvention.Order("domain", 2, ["b.md", "a.md"]));
        Assert.Equal(["a.md", "b.md"], DevbookReadingConvention.Order(null, 0, ["b.md", "a.md"]));
    }

    [Theory]
    [InlineData("domain", 0, "context-map.md")]
    [InlineData(".domain", 0, "context-map.md")]
    [InlineData("domain", 1, "context.md")]
    [InlineData("tech", 0, "technology-graph.md")]
    [InlineData("design", 0, "README.md")]
    [InlineData("ai", 0, "adoption-map.md")]
    public void The_convention_names_each_folder_s_root(string kind, int depth, string root)
    {
        Assert.Equal(root, DevbookReadingConvention.For(kind, depth)?.Root);
    }

    [Theory]
    [InlineData("arc42", 0)]
    [InlineData("arc42", 1)]
    [InlineData("domain", 2)]
    [InlineData("tech", 1)]
    [InlineData("backlog", 0)]
    [InlineData("", 0)]
    public void The_convention_says_nothing_about_other_directories(string kind, int depth)
    {
        Assert.Null(DevbookReadingConvention.For(kind, depth));
    }

    [Fact]
    public void A_missing_convention_root_leaves_the_directory_without_one()
    {
        var order = DevbookReadingConvention.Order("tech", 0, ["tooling.md", "shared.md", "cloud.md"]);

        Assert.Equal(["shared.md", "cloud.md", "tooling.md"], order);
    }

    [Theory]
    [InlineData("01-introduction-and-goals.md", 1L)]
    [InlineData("0007-use-postgres.md", 7L)]
    [InlineData("ADR-0007-use-postgres.md", 7L)]
    [InlineData("2024-review.md", 2024L)]
    [InlineData("01.md", 1L)]
    [InlineData("12", 12L)]
    [InlineData(".devbook/arc42/adr/0003-x.md", 3L)]
    [InlineData("introduction.md", null)]
    [InlineData("v2.md", null)]
    [InlineData("adr", null)]
    public void A_filename_number_is_read_as_the_generator_reads_it(string name, long? number)
    {
        Assert.Equal(number, DevbookReadingConvention.FileNumber(name));
    }

    [Theory]
    [InlineData("domain.order.md", "domain.md")]
    [InlineData("features.checkout.md", "features.md")]
    [InlineData("domain.md", null)]
    [InlineData("domain.order.invariants.md", null)]
    public void A_split_file_names_its_base(string name, string? @base)
    {
        Assert.Equal(@base, DevbookReadingConvention.SplitBase(name));
    }

    [Fact]
    public void The_committed_technology_folder_reads_in_the_convention_s_order()
    {
        // Pointed at the real corpus: the technology panel orders its layers by
        // this, and the folder no longer carries a `_reading-order.json` to fall
        // back on.
        var folder = Backlog.Tests.RepositoryRoot.Directory(".devbook", "tech");
        var names = Directory.GetFiles(folder, "*.md").Select(Path.GetFileName).OfType<string>();

        var order = DevbookReadingConvention.Order("tech", 0, names);

        Assert.Equal("technology-graph.md", order[0]);
        Assert.Equal("shared.md", order[1]);
        Assert.Equal("tooling.md", order[^1]);
    }
}
