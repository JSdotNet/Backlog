namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The outline the builder writes, derived from the folder convention the way the
/// devbook generator's <c>outline.mjs</c> derives it — with the rules only a parse
/// can apply layered on top: <c>index: root</c>, <c>index: exclude</c> and a
/// <c>number</c> field.
///
/// <para>Nothing about the order is authored (local ADR 0016). The fixture carries
/// a <c>_reading-order.json</c> at every level, each declaring an order the
/// convention disagrees with, and none of them shows.</para>
/// </summary>
public sealed class DevbookOutlineConventionTests : IDisposable
{
    private const string StrayOrder = """{ "version": 1, "directories": { ".": { "order": [".devbook/ai", ".devbook/arc42"] } } }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-outline-convention", Guid.NewGuid().ToString("N"));
    private readonly string _database;

    public DevbookOutlineConventionTests()
    {
        _database = Path.Combine(_root, "out", "devbook.db");

        Write(".devbook/_reading-order.json", StrayOrder);

        Write(".devbook/arc42/_reading-order.json", StrayOrder);
        Write(".devbook/arc42/10-quality.md", "# 10. Quality\n");
        Write(".devbook/arc42/02-constraints.md", "# 02. Constraints\n");
        Write(".devbook/arc42/01-introduction.md", "# 01. Introduction\n");
        Write(".devbook/arc42/adr/README.md", "# Architecture Decision Records\n");
        Write(".devbook/arc42/adr/0002-second.md", "# ADR 0002: Second\n");
        Write(".devbook/arc42/adr/0001-first.md", "# ADR 0001: First\n");
        Write(".devbook/arc42/adr/renumbered.md", "# Renumbered\n\n```meta\nnumber: 3\n```\n");
        Write(".devbook/arc42/tdr/README.md", "# Technical Debt Records\n\n```meta\nindex: root\n```\n");
        Write(".devbook/arc42/tdr/0001-debt.md", "# TDR 0001: Debt\n");
        Write(".devbook/arc42/tdr/0002-hidden.md", "# TDR 0002: Hidden\n\n```meta\nindex: exclude\n```\n");

        Write(".devbook/domain/_reading-order.json", StrayOrder);
        Write(".devbook/domain/context-map.md", "# Context Map\n");
        Write(".devbook/domain/zeta/context.md", "# Zeta\n");
        Write(".devbook/domain/zeta/model.md", "# Zeta\n\n```meta\ntype: aggregate\nstatus: draft\n```\n");
        Write(".devbook/domain/zeta/domain.md", "# Zeta\n");
        Write(".devbook/domain/zeta/domain.invariants.md", "# Zeta\n");
        Write(".devbook/domain/zeta/domain.order.md", "# Zeta\n");
        Write(".devbook/domain/zeta/notes.md", "# Zeta\n");
        Write(".devbook/domain/alpha/features.md", "# Alpha\n");

        Write(".devbook/tech/_reading-order.json", StrayOrder);
        Write(".devbook/tech/tooling.md", "# Tooling\n");
        Write(".devbook/tech/cloud.md", "# Cloud\n");
        Write(".devbook/tech/technology-graph.md", "# Technology Graph\n");
        Write(".devbook/tech/shared.md", "# Shared\n\n```meta\nstatus: adopted\n```\n");

        Write(".devbook/design/README.md", "# Design\n");
        Write(".devbook/design/content-editing.md", "# Content Editing\n");
        Write(".devbook/design/color-scheme.md", "# Color Scheme\n");
        Write(".devbook/design/design-principles.md", "# Design Principles\n");

        Write(".devbook/ai/concepts.md", "# Concepts\n");
        Write(".devbook/ai/02-code.md", "# Code\n");
        Write(".devbook/ai/adoption-map.md", "# Adoption Map\n");
        Write(".devbook/ai/01-plan.md", "# Plan\n");

        Assert.True(DevbookDatabaseBuilder.Build(_root, _database, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_areas_come_in_the_fixed_folder_order()
    {
        var areas = Top(".");

        Assert.Equal([".devbook/arc42", ".devbook/domain", ".devbook/tech", ".devbook/design", ".devbook/ai"], areas.Select(row => row.Path));
        Assert.All(areas, row => Assert.Equal("area", row.Type));
        Assert.Equal(["arc42", "domain", "tech", "design", "ai"], areas.Select(row => row.Kind));

        // An area is titled by its root document; arc42 names none.
        Assert.Equal([".devbook/arc42", "Context Map", "Technology Graph", "Design", "Adoption Map"], areas.Select(row => row.Title));
    }

    [Fact]
    public void A_numbered_directory_reads_by_number_and_then_by_name()
    {
        Assert.Equal(["01-introduction.md", "02-constraints.md", "10-quality.md", "adr", "tdr"], Names(".devbook/arc42", ".devbook/arc42"));
    }

    [Fact]
    public void A_number_field_wins_over_the_filename()
    {
        Assert.Equal(["0001-first.md", "0002-second.md", "renumbered.md", "README.md"], Names(".devbook/arc42", ".devbook/arc42/adr"));
    }

    [Fact]
    public void An_index_root_leads_its_directory_and_an_index_exclude_is_left_out()
    {
        var rows = Children(".devbook/arc42", ".devbook/arc42/tdr");

        Assert.Equal(["README.md", "0001-debt.md"], rows.Select(row => row.Name));
        Assert.True(rows[0].IsRoot);

        // The directory takes its root document's title.
        Assert.Equal("Technical Debt Records", Children(".devbook/arc42", ".devbook/arc42").Single(row => row.Name == "tdr").Title);
    }

    [Fact]
    public void The_domain_folder_reads_its_context_map_and_then_its_contexts()
    {
        Assert.Equal(["context-map.md", "alpha", "zeta"], Names(".devbook/domain", ".devbook/domain"));
        Assert.True(Children(".devbook/domain", ".devbook/domain")[0].IsRoot);
    }

    [Fact]
    public void A_bounded_context_reads_in_the_convention_s_slots()
    {
        Assert.Equal(
            ["context.md", "domain.md", "domain.invariants.md", "domain.order.md", "model.md", "notes.md"],
            Names(".devbook/domain", ".devbook/domain/zeta"));

        // A context without its root document still lists everything it has.
        Assert.Equal(["features.md"], Names(".devbook/domain", ".devbook/domain/alpha"));
    }

    [Fact]
    public void The_technology_folder_pins_shared_first_and_tooling_last()
    {
        Assert.Equal(["technology-graph.md", "shared.md", "cloud.md", "tooling.md"], Names(".devbook/tech", ".devbook/tech"));
    }

    [Fact]
    public void The_design_and_ai_folders_read_in_their_convention_s_order()
    {
        Assert.Equal(["README.md", "design-principles.md", "color-scheme.md", "content-editing.md"], Names(".devbook/design", ".devbook/design"));
        Assert.Equal(["adoption-map.md", "01-plan.md", "02-code.md", "concepts.md"], Names(".devbook/ai", ".devbook/ai"));
    }

    [Fact]
    public void A_file_s_status_resolves_to_its_folder_s_resting_value_and_its_kind_to_its_type()
    {
        var zeta = Children(".devbook/domain", ".devbook/domain/zeta");
        var model = zeta.Single(row => row.Name == "model.md");
        var context = zeta.Single(row => row.Name == "context.md");

        Assert.Equal("draft", model.Status);
        Assert.Equal("aggregate", model.Kind);
        Assert.Equal("active", context.Status);
        Assert.Equal("domain", context.Kind);

        var tech = Children(".devbook/tech", ".devbook/tech");
        Assert.Equal("adopted", tech.Single(row => row.Name == "shared.md").Status);
        Assert.Null(tech.Single(row => row.Name == "cloud.md").Status);
    }

    [Fact]
    public void The_repository_scope_nests_the_same_order_under_each_area()
    {
        using var database = Open();
        var rows = database.Outline(".");
        var tech = rows.Single(row => row.Path == ".devbook/tech" && row.Type == "area");

        Assert.Equal(
            ["technology-graph.md", "shared.md", "cloud.md", "tooling.md"],
            rows.Where(row => row.ParentId == tech.Id).OrderBy(row => row.Ordinal).Select(row => row.Name));
    }

    private IReadOnlyList<DevbookOutlineRow> Top(string scope)
    {
        using var database = Open();
        return [.. database.Outline(scope).Where(row => row.ParentId is null).OrderBy(row => row.Ordinal)];
    }

    private List<string> Names(string scope, string directory) => [.. Children(scope, directory).Select(row => row.Name)];

    /// <summary>The rows directly under <paramref name="directory"/> in
    /// <paramref name="scope"/>, in written order — the scope's own top level when
    /// the directory is the scope.</summary>
    private List<DevbookOutlineRow> Children(string scope, string directory)
    {
        using var database = Open();
        var rows = database.Outline(scope);
        var parent = directory == scope ? null : (long?)rows.Single(row => row.Path == directory).Id;

        return [.. rows.Where(row => row.ParentId == parent).OrderBy(row => row.Ordinal)];
    }

    private DevbookDatabase Open() =>
        DevbookDatabase.TryOpen(_database) ?? throw new InvalidOperationException("The builder wrote no readable database.");

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
