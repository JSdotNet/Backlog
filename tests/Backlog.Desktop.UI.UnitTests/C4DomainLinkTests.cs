using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The way across from a domain chapter to the architecture model.
/// <para>
/// This is the whole of what <c>.domain</c> gets from the C4 feature, and it was the
/// deliberate limit: Structurizr has no vocabulary for what a domain chapter says —
/// no aggregate root, no value object, no multiplicity — so there is no C4 model to
/// put here, only a link to the one under <c>.arc42</c>.
/// </para>
/// <para>
/// It is asserted as a hand-off rather than as a picture, because that is what it is.
/// A C4 view is not a file in the knowledge menu, so this panel cannot open one; it
/// names the view to the host, which switches to the architecture section and opens it
/// there. What the panel owes is a control that carries the right target.
/// </para>
/// </summary>
public sealed class C4DomainLinkTests : IDisposable
{
    private const string ContextMap = """
        # Backlog

        ```meta
        type: context-map
        status: draft
        ```

        The strategic view.
        """;

    private const string Workspace = """
        workspace "Test Backlog" "A workspace for the domain link tests" {
            model {
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI"
                }
            }
            views {
                container backlog "containers-backlog" "Container Diagram" { include * }
            }
        }
        """;

    private const string DocumentsTheContextMap = """
        {
          "views": {
            "containers-backlog": [".domain/context-map.md"]
          }
        }
        """;

    private const string DocumentsSomethingElse = """
        {
          "views": {
            "containers-backlog": [".arc42/05-building-block-view.md#container-view"]
          }
        }
        """;

    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_domain_chapter_offers_the_C4_views_that_document_it()
    {
        await using var harness = CreateHarness(references: DocumentsTheContextMap);

        var component = harness.Render(".domain/context-map.md");
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='domain-c4-view']"));
        Assert.Contains("Container Diagram", component.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hand-off. The host is given an arc42 target naming the view, which is what
    /// lets the pane switch section and open it — the panel has nowhere to draw one.
    /// </summary>
    [Fact]
    public async Task Following_it_hands_the_host_an_arc42_target_naming_the_view()
    {
        await using var harness = CreateHarness(references: DocumentsTheContextMap);

        var asked = new List<KnowledgeChapterLink>();
        var component = harness.Render(".domain/context-map.md", asked);
        harness.Settle(component);

        component.Find("[data-testid='domain-c4-view']").Click();

        var target = Assert.Single(asked);
        Assert.Equal("arc42", target.AreaKey);
        Assert.True(target.IsC4View);
        Assert.Equal(".arc42/_c4/backlog.dsl#containers-backlog", target.Reference);
    }

    [Fact]
    public async Task A_domain_chapter_no_view_documents_is_offered_nothing()
    {
        await using var harness = CreateHarness(references: DocumentsSomethingElse);

        var component = harness.Render(".domain/context-map.md");
        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='domain-c4-view']"));
    }

    [Fact]
    public async Task With_the_key_off_a_domain_chapter_is_offered_nothing()
    {
        await using var harness = CreateHarness(references: DocumentsTheContextMap, c4Enabled: false);

        var component = harness.Render(".domain/context-map.md");
        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='domain-c4-view']"));
    }

    /// <summary>
    /// Every domain panel test written before this feature existed, and the storybook
    /// page. The store is resolved from the provider, so a host that never registered
    /// it renders the panel it always did.
    /// </summary>
    [Fact]
    public async Task Without_the_store_registered_the_domain_panel_still_renders()
    {
        await using var harness = CreateHarness(references: DocumentsTheContextMap, registerStore: false);

        var component = harness.Render(".domain/context-map.md");
        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='domain-c4-view']"));
        Assert.Contains("Backlog", component.Markup, StringComparison.Ordinal);
    }

    private Harness CreateHarness(string references, bool c4Enabled = true, bool registerStore = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-c4-domain-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        Directory.CreateDirectory(Path.Combine(root, ".domain"));
        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), ContextMap);

        var c4 = Path.Combine(root, ".arc42", C4KnowledgeStore.WorkspaceDirectory);
        Directory.CreateDirectory(c4);
        File.WriteAllText(Path.Combine(c4, "backlog.dsl"), Workspace);
        File.WriteAllText(Path.Combine(c4, C4KnowledgeStore.ReferenceFile), references);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        if (c4Enabled) Assert.Null(features.SetEnabled(KnowledgeFeatures.C4Diagrams, true));

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var folders = new KnowledgeFolderSource(gitHub, settings);
        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(folders);
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        if (registerStore) context.Services.AddSingleton<C4KnowledgeStore>();

        return new Harness(context, repository.Alias);
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<DomainKnowledgePanel> Render(string? selectedPath, List<KnowledgeChapterLink>? asked = null) =>
            Context.Render<DomainKnowledgePanel>(parameters =>
            {
                parameters
                    .Add(panel => panel.RepositoryAlias, RepositoryAlias)
                    .Add(panel => panel.SelectedPath, selectedPath);

                if (asked is not null)
                {
                    parameters.Add(panel => panel.OnNavigateToChapter, EventCallback.Factory.Create<KnowledgeChapterLink>(this, asked.Add));
                }
            });

        /// <summary>The folder read and the workspace read are both asynchronous, so the
        /// chapter arrives on a later render than the first.</summary>
        public void Settle(IRenderedComponent<DomainKnowledgePanel> component) =>
            component.WaitForAssertion(() =>
                Assert.NotEmpty(component.FindAll("[data-testid='domain-chapter-file']")));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A temp folder that will not delete is not a test failure.
            }
        }
    }
}
