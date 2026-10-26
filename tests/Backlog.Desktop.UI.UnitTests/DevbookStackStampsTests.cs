using Backlog.Desktop.UI.Devbook;
using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook tab reads only the stamps under <c>components</c> in
/// <c>.devbook/config.json</c>. These tests pin that reading — the repository's
/// own file included, so a re-stamp that moves a version shows up here — and the
/// join from a component to the AI usage chapters that depend on its
/// <c>.devbook/tech/ai-development.md</c> entry.
/// </summary>
public sealed class DevbookStackStampsTests : IDisposable
{
    private readonly List<string> _roots = [];

    /// <summary>
    /// The expected values are re-derived from the file itself with
    /// <c>node -e "const c=require('./.devbook/config.json').components;for(const [k,v] of Object.entries(c))console.log(k,v.pluginVersion)"</c>;
    /// a re-stamp updates them here in the same change.
    /// </summary>
    [Fact]
    public void The_repository_s_own_configuration_reads_as_its_stamps()
    {
        var stamps = DevbookStackStamps.Read(RepositoryRoot.Root.FullName);

        Assert.NotNull(stamps);
        Assert.Equal("1.28.0", stamps.Delivery?.PluginVersion);
        Assert.Equal("1.30.1", stamps.Devbook?.PluginVersion);
        Assert.Equal("1.26.0", stamps.Schedule?.PluginVersion);

        var devbook = stamps.Devbook!;
        Assert.Equal("36", devbook.ContractVersion);
        Assert.Equal(["arc42", "domain", "tech", "design", "ai"], devbook.Adopted);
        Assert.Equal(["run", "capture", "diagnose", "estimate"], devbook.Procedures);
        Assert.Equal(44, devbook.MaterializedFiles);
        Assert.Equal(39, devbook.ManagedFiles);
        Assert.Equal(5, devbook.UnmanagedFiles);
        Assert.Equal(18, devbook.Migrations.Count);
        Assert.Equal(new DevbookMigrationStamp("035-delivery-path-stages", "2026-10-26"), devbook.Migrations[^1]);

        Assert.Equal(11, stamps.Schedule!.Enabled.Count);
        Assert.Contains("issue-sweep", stamps.Schedule.Enabled);
    }

    [Fact]
    public void A_missing_component_is_not_adopted_and_the_rest_still_read()
    {
        var stamps = DevbookStackStamps.Parse("""
            { "components": { "devbook": { "pluginVersion": "1.0.0", "adopted": ["arc42"] } } }
            """);

        Assert.NotNull(stamps);
        Assert.Null(stamps.Delivery);
        Assert.Null(stamps.Schedule);
        Assert.Equal("1.0.0", stamps.Devbook?.PluginVersion);
        Assert.Null(stamps.Devbook?.ContractVersion);
        Assert.Empty(stamps.Devbook!.Procedures);
        Assert.Empty(stamps.Devbook.Migrations);
        Assert.Equal(0, stamps.Devbook.MaterializedFiles);
    }

    [Fact]
    public void A_configuration_with_no_components_adopts_nothing()
    {
        var stamps = DevbookStackStamps.Parse("""{ "id": "backlog" }""");

        Assert.Equal(new DevbookStackStamps(null, null, null), stamps);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Text_that_is_not_a_configuration_reads_as_none(string json) =>
        Assert.Null(DevbookStackStamps.Parse(json));

    [Fact]
    public void A_repository_with_no_configuration_reads_as_none()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-stack-stamps-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        _roots.Add(root);

        Assert.Null(DevbookStackStamps.Read(root));
        Assert.Null(DevbookStackStamps.Read(null));
        Assert.Null(DevbookStackStamps.Load(null, ".ai", "backlog"));
    }

    [Fact]
    public void Only_an_entry_flagged_managed_counts_as_managed()
    {
        var stamps = DevbookStackStamps.Parse("""
            { "components": { "devbook": { "materialized": {
                "a": { "managed": true },
                "b": { "managed": false },
                "c": { "from": "1.0.0" }
            } } } }
            """);

        Assert.Equal(1, stamps!.Devbook!.ManagedFiles);
        Assert.Equal(2, stamps.Devbook.UnmanagedFiles);
    }

    [Fact]
    public void A_component_works_where_the_chapters_depending_on_its_tech_entry_sit()
    {
        var model = AiModel.From([
            File("05-unattended-runs.md",
                Section("Issue sweep", stage: ["code", "verify"], layer: ["upkeep"], dependsOn: [".devbook/tech/ai-development.md#delivery-schedule"]),
                Section("Merge review", stage: ["integrate"], dependsOn: [".devbook/tech/ai-development.md#delivery-schedule", ".devbook/tech/ai-development.md#devbook-plugin"]),
                Section("Root layout", stage: ["decide"], dependsOn: [".tech/ai-development.md#delivery-engine"]),
                Section("Other entry", stage: ["code"], dependsOn: [".devbook/tech/ai-development.md#claude-code"]),
                Section("Other file", stage: ["code"], dependsOn: [".devbook/tech/tooling.md#delivery-engine"]))
        ]);

        Assert.Equal(["Issue sweep", "Merge review"], DevbookStackUsages.Of(model, "schedule").Select(usage => usage.Heading));
        Assert.Equal(["Merge review"], DevbookStackUsages.Of(model, "devbook").Select(usage => usage.Heading));
        Assert.Equal(["Root layout"], DevbookStackUsages.Of(model, "delivery").Select(usage => usage.Heading));
        Assert.Empty(DevbookStackUsages.Of(model, "openspec"));
    }

    [Theory]
    [InlineData("arc42", "arc42")]
    [InlineData("ai", "ai")]
    [InlineData(".tech", "tech")]
    public void An_adopted_folder_links_to_its_section(string folder, string areaKey)
    {
        var link = DevbookChapterLink.ForFolder(folder);

        Assert.NotNull(link);
        Assert.Equal(areaKey, link.AreaKey);
        Assert.True(link.IsFolder);
    }

    [Theory]
    [InlineData("openspec")]
    [InlineData("")]
    [InlineData("arc42/adr")]
    public void A_folder_the_pane_has_no_section_for_has_no_link(string folder) =>
        Assert.Null(DevbookChapterLink.ForFolder(folder));

    [Fact]
    public void A_chapter_link_is_not_a_folder() =>
        Assert.False(DevbookChapterLink.From(".devbook/arc42/01-introduction-and-goals.md")!.IsFolder);

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static DocumentDevbookFile File(string name, params DocumentDevbookSection[] sections) =>
        new(name, name, string.Empty, MetadataRecord.Empty, sections);

    internal static DocumentDevbookSection Section(string heading, string[]? stage = null, string[]? layer = null, string[]? dependsOn = null)
    {
        var extra = new Dictionary<string, IReadOnlyList<string>>();
        if (stage is not null) extra["stage"] = stage;
        if (layer is not null) extra["layer"] = layer;

        var meta = new MetadataRecord
        {
            Status = "adopted",
            Type = "practice",
            Extra = extra,
            DependsOn = [.. (dependsOn ?? []).Select(raw => DevbookReference.Parse(raw)!)]
        };

        return new DocumentDevbookSection(heading, $"ai-{heading}", meta, []);
    }
}
