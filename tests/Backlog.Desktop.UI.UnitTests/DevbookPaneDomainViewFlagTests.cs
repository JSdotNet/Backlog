using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The New domain view switch (<c>devbook-domain-view</c>), read by the pane's
/// Domain area and nowhere else.
///
/// <para>Off — its default — the Domain area is the panel it has always been: the
/// same component, handed the same parameters, opening on the same chapter. That
/// is the requirement "The flag off keeps today's Domain view", and the first test
/// here is its proof. On, the area is the <see cref="DomainView"/> frame, which
/// names the page the selected menu row opens (local ADR 0037).</para>
/// </summary>
public sealed class DevbookPaneDomainViewFlagTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public void The_switch_ships_off()
    {
        var definition = Assert.Single(AppFeatures.All, feature => feature.Key == DevbookFeatures.DomainView);

        Assert.Equal("devbook-domain-view", definition.Key);
        Assert.False(definition.EnabledByDefault);
        Assert.Equal(AppFeatureStatus.Dev, definition.Status);
    }

    [Fact]
    public async Task With_the_switch_off_the_Domain_area_is_todays_panel()
    {
        await using var harness = CreateHarness(domainView: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));

        // The panel, with the parameters the pane has always handed it.
        var panel = component.FindComponent<DomainDevbookPanel>().Instance;
        Assert.True(panel.Compact);
        Assert.False(panel.ShowContextTabs);
        Assert.Equal(harness.RepositoryAlias, panel.RepositoryAlias);
        Assert.Equal("context-map.md", panel.SelectedPath);
        Assert.Empty(component.FindComponents<DomainView>());
        Assert.Empty(component.FindAll("[data-testid='domain-view']"));

        // And it reads as it did: a context folder still opens the context overview.
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-context']")));
        Assert.Empty(component.FindAll("[data-testid='domain-view']"));
    }

    [Fact]
    public async Task With_the_switch_on_the_Domain_area_is_the_domain_view_frame()
    {
        await using var harness = CreateHarness(domainView: true);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal("context-map", component.Find("[data-testid='domain-view']").GetAttribute("data-page")));
        Assert.Empty(component.FindComponents<DomainDevbookPanel>());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='context-map-page']")));

        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.Equal("context", component.Find("[data-testid='domain-view']").GetAttribute("data-page")));

        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain")));
        await ClickMenuItemAsync(component, "Domain");
        component.WaitForAssertion(() => Assert.Equal("domain", component.Find("[data-testid='domain-view']").GetAttribute("data-page")));
        Assert.Equal("tasks/domain.md", component.FindComponent<DomainView>().Instance.SelectedPath);
    }

    [Fact]
    public async Task Flipping_the_switch_swaps_the_open_Domain_area()
    {
        await using var harness = CreateHarness(domainView: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        Assert.Null(harness.Features.SetEnabled(DevbookFeatures.DomainView, true));
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")));
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file']"));

        Assert.Null(harness.Features.SetEnabled(DevbookFeatures.DomainView, false));
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));
        Assert.Empty(component.FindAll("[data-testid='domain-view']"));
    }

    /// <summary>The menu is part of what the switch changes (local ADR 0037): off,
    /// a split file is a row of its own beside its kind; on, it is a child of its
    /// kind's row. Flipping the switch reloads the open menu into the other
    /// listing, without a folder change to prompt it.</summary>
    [Fact]
    public async Task Flipping_the_switch_relists_the_open_Domain_menu()
    {
        await using var harness = CreateHarness(domainView: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")));
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain.order")));
        Assert.Null(MenuItem(component, "Order"));

        Assert.Null(harness.Features.SetEnabled(DevbookFeatures.DomainView, true));
        component.WaitForAssertion(() => Assert.Null(MenuItem(component, "Domain.order")));

        await ClickMenuItemAsync(component, "Domain");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Order")));

        Assert.Null(harness.Features.SetEnabled(DevbookFeatures.DomainView, false));
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain.order")));
        Assert.Null(MenuItem(component, "Order"));
    }

    /// <summary>An invariants subpage has a row in the flat list and none in the
    /// listing by unit, where it is its domain page's Always true tab. Open when
    /// the switch flips on, it hands the selection to that page rather than
    /// letting the pane fall back to the context map.</summary>
    [Fact]
    public async Task Flipping_the_switch_on_with_an_invariants_subpage_open_lands_on_its_domain_page()
    {
        await using var harness = CreateHarness(domainView: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")));
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Order invariants")));
        await ClickMenuItemAsync(component, "Order invariants");
        component.WaitForAssertion(() => Assert.Equal("tasks/domain.order.invariants.md", component.FindComponent<DomainDevbookPanel>().Instance.SelectedPath));

        Assert.Null(harness.Features.SetEnabled(DevbookFeatures.DomainView, true));
        component.WaitForAssertion(() => Assert.Equal("tasks/domain.order.md", component.FindComponent<DomainView>().Instance.SelectedPath));
        component.WaitForAssertion(() => Assert.Null(MenuItem(component, "Order invariants")));
        Assert.NotNull(MenuItem(component, "Order"));
    }

    /// <summary>The Domain row opens one unit at a time (local ADR 0037 §4): an
    /// unsplit domain.md offers its units in a chapter bar, and a requirement a
    /// domain service lists opens the Requirements page.</summary>
    [Fact]
    public async Task With_the_switch_on_Domain_picks_a_unit_and_a_requirement_opens_requirements()
    {
        await using var harness = CreateHarness(domainView: true);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")));
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain")));
        await ClickMenuItemAsync(component, "Domain");

        component.WaitForAssertion(() => Assert.Equal(
            ["Task", "Planning"],
            component.FindAll("[data-testid='domain-units-bar'] [role='tab']").Select(tab => tab.TextContent.Trim())));
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));

        await component.InvokeAsync(() => component.Find("[data-testid='domain-unit-tab-planning']").Click());
        component.WaitForAssertion(() => Assert.Equal("A plan is kept", component.Find("[data-testid='domain-unit-guarantee-link']").TextContent.Trim()));

        await component.InvokeAsync(() => component.Find("[data-testid='domain-unit-guarantee-link']").Click());
        component.WaitForAssertion(() => Assert.Equal("tasks/requirements.md", component.FindComponent<DomainView>().Instance.SelectedPath));
        Assert.Equal("requirements", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
    }

    /// <summary>A split domain file opens on its one unit, with no chapter bar:
    /// the menu row was the picking.</summary>
    [Fact]
    public async Task With_the_switch_on_a_split_domain_file_opens_on_its_unit()
    {
        await using var harness = CreateHarness(domainView: true);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")));
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Domain")));
        await ClickMenuItemAsync(component, "Domain");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Order")));
        await ClickMenuItemAsync(component, "Order");

        component.WaitForAssertion(() => Assert.Equal("order", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")));
        Assert.Empty(component.FindAll("[data-testid='domain-units-bar']"));
    }

    /// <summary>One menu row, by the label a reader reads; null while it is not drawn.</summary>
    private static IElement? MenuItem(IRenderedComponent<DevbookPane> component, string label) =>
        component.FindAll(".devbook-menu__item")
            .SingleOrDefault(item => string.Equals(item.QuerySelector(".devbook-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    /// <summary>Presses one menu row, found and clicked in one dispatch so a load
    /// landing in between cannot take the row's handler away.</summary>
    private static Task ClickMenuItemAsync(IRenderedComponent<DevbookPane> component, string label) =>
        component.InvokeAsync(() => MenuItem(component, label)!.Click());

    private Harness CreateHarness(bool domainView)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-domain-view-flag", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), "# Domain: Tasks\n\n```meta\nstatus: draft\n```\n\nWhat the tasks context is.\n\n## Task\n\n```meta\ntype: aggregate\n```\n\nA piece of work.\n\n## Planning\n\n```meta\ntype: domain-service\n```\n\nPlans the work.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "requirements.md"), "# Requirements: Tasks\n\n## Planning\n\n```meta\ntype: requirements\nrelated: [.domain/tasks/domain.md#planning]\n```\n\n### Requirement: A plan is kept\n\n```meta\ntype: requirement\n```\n\nThe system SHALL keep a plan.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.order.md"), "# Order\n\nOne aggregate of the tasks context.\n\n## Order\n\n```meta\ntype: aggregate\n```\n\nA request for goods.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.order.invariants.md"), "# Invariants\n\nWhat an order always keeps.\n");
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, true);
        if (domainView) _ = features.SetEnabled(DevbookFeatures.DomainView, true);

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
        context.Services.AddSingleton<IAppFeatureSettings>(features);
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

        return new Harness(context, repository.Alias, features);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias, IAppFeatureSettings Features) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
