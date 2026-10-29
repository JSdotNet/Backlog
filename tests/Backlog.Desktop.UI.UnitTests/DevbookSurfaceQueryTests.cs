using Backlog.Desktop.UI.Devbook.Extensions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook surface opened on <c>chapter=&lt;path&gt;</c> (local ADR 0043 §6), as a
/// popped-out window, a <c>backlog://</c> link or the <c>open_window</c> tool opens
/// it: <c>scope=&lt;owner/name&gt;</c> reads that repository's chapter,
/// <c>scope=brain</c> the Brain note at that path, and no <c>scope</c> whatever the
/// window is scoped to.
/// </summary>
public sealed class DevbookSurfaceQueryTests
{
    private const string BuildingBlocks = ".arc42/05-building-block-view.md";

    [Fact]
    public async Task A_scope_naming_a_repository_by_owner_and_name_opens_that_repositorys_chapter()
    {
        await using var harness = CreateHarness();

        var surface = Render(harness, new()
        {
            [DevbookSurfaceQuery.Chapter] = BuildingBlocks,
            [DevbookSurfaceQuery.Scope] = "JSdotNet/Backlog",
        });

        WaitForRepositoryChapter(surface);
        Assert.Empty(surface.FindAll("[data-testid='devbook-brain-badge']"));
    }

    [Fact]
    public async Task A_scope_naming_the_brain_opens_the_note_at_that_path_in_the_brain_folder()
    {
        await using var harness = CreateHarness();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/standup.md", "# Standup\n\nToday.\n");
        harness.WriteNote("work/plan.md", "# Plan\n\nThe plan's own words.\n");

        var surface = Render(harness, new()
        {
            [DevbookSurfaceQuery.Chapter] = "work/plan.md",
            [DevbookSurfaceQuery.Scope] = DevbookSurfaceQuery.Brain,
        });

        surface.WaitForAssertion(() =>
        {
            Assert.NotEmpty(surface.FindAll("[data-testid='devbook-brain-badge']"));
            Assert.Contains("The plan's own words.", surface.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_scope_naming_the_brain_with_no_folder_set_asks_for_one()
    {
        await using var harness = CreateHarness(brainFolder: null);

        var surface = Render(harness, new()
        {
            [DevbookSurfaceQuery.Chapter] = "work/plan.md",
            [DevbookSurfaceQuery.Scope] = DevbookSurfaceQuery.Brain,
        });

        surface.WaitForElement("[data-testid='devbook-brain-first-use']");
        Assert.NotEmpty(surface.FindAll("[data-testid='devbook-brain-set-up']"));
        Assert.Empty(surface.FindAll("[data-testid='brain-note']"));
    }

    [Fact]
    public async Task No_scope_reads_the_windows_repository_scope()
    {
        await using var harness = CreateHarness();
        harness.Context.Services.AddSingleton<IDevbookWindowScope>(new FixedWindowScope(harness.RepositoryAlias));

        var surface = Render(harness, new() { [DevbookSurfaceQuery.Chapter] = BuildingBlocks });

        WaitForRepositoryChapter(surface);
        Assert.Empty(surface.FindAll("[data-testid='devbook-brain-badge']"));
    }

    [Fact]
    public async Task No_scope_in_a_window_scoped_to_the_brain_opens_the_brain_note()
    {
        await using var harness = CreateHarness();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/plan.md", "# Plan\n\nThe plan's own words.\n");
        harness.Context.Services.AddSingleton<IDevbookWindowScope>(new FixedWindowScope(BrainScope.Value));

        var surface = Render(harness, new() { [DevbookSurfaceQuery.Chapter] = "work/plan.md" });

        surface.WaitForAssertion(() =>
            Assert.Contains("The plan's own words.", surface.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal));
    }

    /// <summary>A host that composes no window scope leaves a scopeless Devbook on
    /// the Brain, as the port says.</summary>
    [Fact]
    public async Task No_scope_and_no_window_scope_reads_the_brain()
    {
        await using var harness = CreateHarness();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/plan.md", "# Plan\n\nThe plan's own words.\n");

        var surface = Render(harness, new() { [DevbookSurfaceQuery.Chapter] = "work/plan.md" });

        surface.WaitForAssertion(() =>
            Assert.Contains("The plan's own words.", surface.Find("[data-testid='brain-note']").TextContent, StringComparison.Ordinal));
        Assert.Empty(surface.FindAll("[data-testid='arc42-chapter-file']"));
    }

    private static DevbookPaneBrainHarness CreateHarness(string? brainFolder = "<harness>")
    {
        var harness = brainFolder is null
            ? DevbookPaneBrainHarness.Create(brainFolder: null)
            : DevbookPaneBrainHarness.Create();

        // A second arc42 chapter, so opening it is not the section's first chapter
        // landing on its own.
        var chapter = Path.Combine(harness.Root, "clone", BuildingBlocks.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(chapter, "# Building block view\n\nThe blocks of the system.\n");

        harness.Context.Services.AddDevbookSurface();
        harness.Context.Services.AddScoped<ISurfaceNavigator, SurfaceNavigator>();
        return harness;
    }

    private static IRenderedComponent<DevbookSurface> Render(DevbookPaneBrainHarness harness, Dictionary<string, string> query) =>
        harness.Context.Render<DevbookSurface>(parameters => parameters
            .Add(surface => surface.Query, (IReadOnlyDictionary<string, string>)query));

    private static void WaitForRepositoryChapter(IRenderedComponent<DevbookSurface> surface) =>
        surface.WaitForAssertion(() =>
        {
            Assert.Equal(BuildingBlocks, surface.Find("[data-testid='arc42-chapter-file'] .file-view__path").TextContent);
            Assert.Contains("The blocks of the system.", surface.Find("[data-testid='arc42-chapter-file'] .file-view__body").TextContent, StringComparison.Ordinal);
        });

    private sealed class FixedWindowScope(string repositoryAlias) : IDevbookWindowScope
    {
        public string RepositoryAlias { get; } = repositoryAlias;
    }
}
