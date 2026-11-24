using System.Text.Json;

using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain area is the <see cref="DomainView"/> frame, which names the page the
/// selected menu row opens (local ADR 0047), and nothing else. The New domain view
/// switch (<c>devbook-domain-view</c>) that once chose between it and the old panel
/// is retired: it is no longer in the catalog, a features.json that still names it
/// reads and saves without it, and the view draws with no feature settings composed
/// at all.
/// </summary>
public sealed class DomainViewFlagRetirementTests : IDisposable
{
    /// <summary>The key the retired switch was stored under.</summary>
    private const string RetiredKey = "devbook-domain-view";

    private readonly List<TemporaryDirectory> _temps = [];

    [Fact]
    public void The_retired_switch_is_not_in_the_catalog()
    {
        Assert.DoesNotContain(AppFeatures.All, feature => string.Equals(feature.Key, RetiredKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A features.json written while the switch existed — turned off, or
    /// held in either list by an older build — reads without throwing, keeps the
    /// choices it holds for current features, and the next save writes the file
    /// without the retired key.</summary>
    [Theory]
    [InlineData("disabledFeatures")]
    [InlineData("enabledFeatures")]
    public void A_stored_choice_for_the_retired_switch_is_dropped_on_read_and_save(string list)
    {
        var root = NewRoot();
        var path = Path.Combine(root, "features", "features.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string[]>
        {
            [list] = [RetiredKey]
        }));

        var features = new AppFeatureSettingsStore(AppFeatures.All, path);

        Assert.DoesNotContain(RetiredKey, features.Current.DisabledFeatures);
        Assert.DoesNotContain(RetiredKey, features.Current.EnabledFeatures);

        // Any current switch moved forces the save; Editing ships off, so turning it
        // on is a choice the reloaded file has to carry.
        Assert.Null(features.SetEnabled(DevbookFeatures.Editing, true));

        var saved = File.ReadAllText(path);
        Assert.DoesNotContain(RetiredKey, saved, StringComparison.OrdinalIgnoreCase);
        Assert.True(new AppFeatureSettingsStore(AppFeatures.All, path).IsEnabled(DevbookFeatures.Editing));
    }

    /// <summary>Nothing about the Domain view reads a feature switch any more: a host
    /// that composes no feature settings still draws it, not a failure.</summary>
    [Fact]
    public async Task With_no_feature_settings_composed_the_domain_view_still_draws()
    {
        await using var harness = CreateHarness(composeFeatures: false);
        Assert.Null(harness.Context.Services.GetService<IAppFeatureSettings>());

        var component = harness.Context.Render<DomainView>(parameters => parameters
            .Add(view => view.RepositoryAlias, harness.RepositoryAlias)
            .Add(view => view.SelectedPath, "context-map.md"));

        component.WaitForAssertion(() => Assert.Equal("context-map", component.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='context-map-page']")), Patience);
    }

    /// <summary>The pane, whose scope does compose feature settings, draws the
    /// Domain view in the Domain area even where the stored settings still turn the
    /// retired switch off: there is no other panel left to fall back to.</summary>
    [Fact]
    public async Task With_the_retired_switch_stored_off_the_Domain_area_is_still_the_domain_view()
    {
        await using var harness = CreateHarness(storedRetiredKey: true);

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.Equal("context-map", component.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);
        Assert.Single(component.FindComponents<DomainView>());
    }

    [Fact]
    public async Task The_Domain_area_is_the_domain_view_frame()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal("context-map", component.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='context-map-page']")), Patience);

        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.Equal("context", component.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);

        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain")), Patience);
        await ClickMenuItemAsync(component, "Domain");
        component.WaitForAssertion(() => Assert.Equal("domain", component.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);
        Assert.Equal("tasks/domain.md", component.FindComponent<DomainView>().Instance.SelectedPath);
    }

    /// <summary>The Domain row opens one unit at a time (local ADR 0047 §4): an
    /// unsplit domain.md offers its units in a chapter bar, and a requirement a
    /// domain service lists opens the Requirements page.</summary>
    [Fact]
    public async Task Domain_picks_a_unit_and_a_requirement_opens_requirements()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain")), Patience);
        await ClickMenuItemAsync(component, "Domain");

        component.WaitForAssertion(() => Assert.Equal(
            ["Task", "Planning"],
            component.FindAll("[data-testid='domain-units-bar'] [role='tab']").Select(tab => tab.TextContent.Trim())), Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));

        await component.InvokeAsync(() => component.Find("[data-testid='domain-unit-tab-planning']").Click());
        component.WaitForAssertion(() => Assert.Equal("A plan is kept", component.Find("[data-testid='domain-unit-guarantee-link']").TextContent.Trim()), Patience);

        await component.InvokeAsync(() => component.Find("[data-testid='domain-unit-guarantee-link']").Click());
        component.WaitForAssertion(() => Assert.Equal("tasks/requirements.md", component.FindComponent<DomainView>().Instance.SelectedPath), Patience);
        Assert.Equal("requirements", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
    }

    /// <summary>A split domain file opens on its one unit, with no chapter bar:
    /// the menu row was the picking.</summary>
    [Fact]
    public async Task A_split_domain_file_opens_on_its_unit()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain")), Patience);
        await ClickMenuItemAsync(component, "Domain");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Order")), Patience);
        await ClickMenuItemAsync(component, "Order");

        component.WaitForAssertion(() => Assert.Equal("order", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-units-bar']"));
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>One menu row, by the label a reader reads; null while it is not drawn.</summary>
    private static IElement? MenuItem(IRenderedComponent<DevbookPane> component, string label) =>
        component.FindAll(".devbook-menu__item")
            .SingleOrDefault(item => string.Equals(item.QuerySelector(".devbook-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    /// <summary>Presses one menu row, found and clicked in one dispatch so a load
    /// landing in between cannot take the row's handler away.</summary>
    private static Task ClickMenuItemAsync(IRenderedComponent<DevbookPane> component, string label) =>
        component.InvokeAsync(() => MenuItem(component, label)!.Click());

    private string NewRoot()
    {
        var temp = new TemporaryDirectory("backlog-domain-view-flag-retirement");
        _temps.Add(temp);
        return temp.Path;
    }

    private Harness CreateHarness(bool composeFeatures = true, bool storedRetiredKey = false)
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), "# Domain: Tasks\n\n```meta\nstatus: draft\n```\n\nWhat the tasks context is.\n\n## Task\n\n```meta\ntype: aggregate\n```\n\nA piece of work.\n\n## Planning\n\n```meta\ntype: domain-service\n```\n\nPlans the work.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "requirements.md"), "# Requirements: Tasks\n\n## Planning\n\n```meta\ntype: requirements\nrelated: [.domain/tasks/domain.md#planning]\n```\n\n### Requirement: A plan is kept\n\n```meta\ntype: requirement\n```\n\nThe system SHALL keep a plan.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.order.md"), "# Order\n\nOne aggregate of the tasks context.\n\n## Order\n\n```meta\ntype: aggregate\n```\n\nA request for goods.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.order.invariants.md"), "# Invariants\n\nWhat an order always keeps.\n");
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders = [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        if (composeFeatures)
        {
            var path = Path.Combine(root, "features", "features.json");
            if (storedRetiredKey)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { disabledFeatures = new[] { RetiredKey } }));
            }

            var features = new AppFeatureSettingsStore(AppFeatures.All, path);
            context.Services.AddSingleton<IAppFeatureSettings>(features);
        }

        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context, repository.Alias);
    }

    public void Dispose()
    {
        foreach (var temp in _temps) temp.Dispose();
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
