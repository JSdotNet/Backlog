using AngleSharp.Dom;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The three sections a reference could not be followed out of.
/// <para>
/// Domain and arc42 were wired to the pane's selection; Design, Instructions and
/// Technology were not, so a reader in any of them met a reference that either
/// went to their browser or went nowhere at all. Which section a reader happens to
/// be standing in is not a reason for a link to behave differently, so these prove
/// the same landing from each: the pane's own section strip and the chapter behind
/// it move, exactly as they do when the reference is pressed in a domain chapter.
/// </para>
/// <para>
/// Technology has no chapter selection of its own — it is the one section with no
/// menu beside it — so what is asserted there is the section move, which is the
/// whole of what following a reference out of a node can mean.
/// </para>
/// </summary>
public sealed class KnowledgePaneSectionReferenceTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_reference_in_a_design_chapter_opens_the_chapter_it_names()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.Find("#tab-design").Click();

        // The chapter's own file view, not the section around it: the panel renders
        // that section the moment it exists and fills it once the folder has been
        // read, so waiting on the section is waiting for nothing.
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='design-chapter-file']")));

        Reference(component, ".design/component-libraries.md#materialization").Click();

        component.WaitForAssertion(() => Assert.Contains(
            "Component Libraries",
            component.Find(".knowledge-menu__item--active").TextContent,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_reference_in_an_instruction_file_reaches_the_section_it_names()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.Find("#tab-instructions").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='instructions-document']")));

        // An instruction file's own folder is not a section, and it links into the
        // ones that are — which is the whole reason the panel needed the wiring.
        Reference(component, ".design/color-scheme.md").Click();

        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-design").GetAttribute("aria-selected")));
    }

    [Fact]
    public async Task A_relation_on_a_technology_node_reaches_the_section_it_names()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.Find("#tab-tech").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-layers-tab']")));
        component.Find("[data-testid='technology-layers-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-node']")));

        // It used to be an anchor whose href was the repository path itself, so a
        // relation on a node was a link straight out of the app.
        Reference(component, ".arc42/03-context-and-scope.md").Click();

        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-arc42").GetAttribute("aria-selected")));
    }

    [Theory]
    [InlineData("design", "design-chapter-file")]
    [InlineData("instructions", "instructions-document")]
    [InlineData("tech", "technology-node")]
    public async Task No_section_leaves_a_reference_as_a_link_out_of_the_app(string section, string readyTestId)
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.Find($"#tab-{section}").Click();

        if (section == "tech")
        {
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-layers-tab']")));
            component.Find("[data-testid='technology-layers-tab']").Click();
        }

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll($"[data-testid='{readyTestId}']")));

        // An anchor carrying a repository path is a dead link either way it is
        // read: the app's own origin has no `.design` folder to serve, and a
        // `target="_blank"` on one opens a browser window on the same nothing. So
        // there are none left in any section — a reference is a control the pane
        // handles, or it is text.
        Assert.DoesNotContain(
            component.FindAll("a").Select(anchor => anchor.GetAttribute("href")),
            href => href is not null && href.StartsWith('.'));
    }

    /// <summary>The reference as the reader meets it, found by the path on its
    /// title rather than by position: a chapter holds several, and which one is
    /// pressed is the whole point of the test.</summary>
    private static IElement Reference(IRenderedComponent<KnowledgePane> component, string raw) =>
        component.FindAll("button.knowledge-ref--action")
            .Single(button => string.Equals(button.GetAttribute("title"), raw, StringComparison.Ordinal));

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-section-references", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".design"));
        Directory.CreateDirectory(Path.Combine(root, ".tech"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        Directory.CreateDirectory(Path.Combine(root, ".github", "instructions"));
        _roots.Add(root);

        // The links go in `accessibility.md`, because that is the chapter the pane
        // opens the section on: the menu tree is alphabetical and the first
        // selectable node is what gets selected. Both forms the design folder
        // writes — a sibling with an anchor, and a bare sibling.
        File.WriteAllText(Path.Combine(root, ".design", "accessibility.md"), """
            # Accessibility

            ```meta
            status: accepted
            ```

            Named as a group, see [the component rule](component-libraries.md#materialization),
            and the palette is in [the color scheme](color-scheme.md).
            """);
        File.WriteAllText(Path.Combine(root, ".design", "component-libraries.md"), "# Component libraries\n\n## Materialization\n\nWhy the library is the product's own.\n");
        File.WriteAllText(Path.Combine(root, ".design", "color-scheme.md"), "# Color scheme\n\nThe tokens.\n");

        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        // The root document, because the layer list is read from its `order`.
        File.WriteAllText(Path.Combine(root, ".tech", "technology-graph.md"), """
            # Technology graph

            ```meta
            status: draft
            order: ["shared.md"]
            ```

            Repository technology overview.
            """);

        File.WriteAllText(Path.Combine(root, ".tech", "shared.md"), """
            # Shared Technologies

            ```meta
            status: accepted
            kind: layer
            ```

            Shared platform choices.

            ## .NET

            ```meta
            status: accepted
            kind: runtime
            related: [".arc42/03-context-and-scope.md"]
            ```

            The runtime everything is built on.
            """);

        File.WriteAllText(Path.Combine(root, ".github", "instructions", "ui-components.instructions.md"), """
            ---
            applyTo: "src/**"
            description: Shared components.
            ---

            # UI components

            The palette is in [the color scheme](.design/color-scheme.md).
            """);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = features.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = [.. KnowledgeFolderSetting.Defaults()
                .Select(folder => folder with { Enabled = folder.Key is ".design" or ".tech" or ".arc42" or "instructions" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context, repository.Alias);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Awaited disposal, because the pane renders the editing surface and
    /// that writes its last pending save on the way out.</summary>
    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<KnowledgePane> Render() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
