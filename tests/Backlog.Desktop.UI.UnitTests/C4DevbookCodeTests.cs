using Backlog.Infrastructure.Devbook.Code;
using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams.C4;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Code level of the C4 zoom tab over a fixture devbook in the 1.28.0 layout: a
/// workspace with a Devbook component, a building block that names its code folder in
/// <c>code</c>, a decision record about the component, and C# source in that folder.
/// <para>
/// This repository's own devbook has no building-block file yet, so its components
/// stop at level 3; the fixture is what proves level 4 end to end — catalog, reader,
/// mapping and the panel's zoom tab.
/// </para>
/// </summary>
public sealed class C4DevbookCodeTests : IDisposable
{
    private const string Devbook = "backlog.desktop.devbook";

    private const string Workspace = """
        workspace "Coded" "A workspace for the code tests" {
            !identifiers hierarchical
            model {
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI" {
                        devbook = component "Devbook" "The devbook panels"
                        shell = component "Shell" "Holds the panes"
                    }
                }
                backlog.desktop.shell -> backlog.desktop.devbook "Offers the pane"
            }
            views {
                systemContext backlog "context-backlog" "Context" { include * }
                container backlog "containers-backlog" "Containers" { include * }
                component backlog.desktop "components-desktop" "Desktop App components" { include * }
            }
        }
        """;

    private const string BuildingBlock = """
        # Devbook

        ```meta
        c4: [backlog.desktop.devbook]
        code: [src/Devbook]
        ```

        The panels.
        """;

    private const string Decision = """
        # ADR 0004: Knowledge index is a generated database

        ```meta
        c4: [backlog.desktop.devbook]
        ```

        Prose.
        """;

    private const string Refresher = """
        namespace Shop.Devbook;

        public sealed class Refresher : System.IDisposable
        {
            private readonly Builder _builder = new();

            public void Request(string root) { }

            public void Dispose() { }
        }
        """;

    private const string BuilderSource = """
        namespace Shop.Devbook;

        internal sealed class Builder
        {
            public bool Build(string root) => true;
        }
        """;

    private readonly List<string> _roots = [];

    /// <summary>The fixture repository: the devbook, and the source its building block
    /// names.</summary>
    private string CreateRepository(bool withBuildingBlock = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-c4-code-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        var arc42 = Path.Combine(root, ".devbook", "arc42");
        Write(Path.Combine(arc42, C4DevbookStore.WorkspaceFile), Workspace);
        Write(Path.Combine(arc42, "adr", "0004-knowledge-index.md"), Decision);
        if (withBuildingBlock) Write(Path.Combine(arc42, "building-blocks", "devbook.md"), BuildingBlock);
        Directory.CreateDirectory(Path.Combine(root, ".devbook", "domain"));

        Write(Path.Combine(root, "src", "Devbook", "Refresher.cs"), Refresher);
        Write(Path.Combine(root, "src", "Devbook", "Builder.cs"), BuilderSource);
        return root;

        static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    private (AppFeatureSettingsStore Features, GitHubSettingsStore GitHub, DevbookFolderSource Folders, WorkspaceSettingsStore Workspace, string Alias) Sources(string clone)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "backlog-c4-code-settings-" + Guid.NewGuid().ToString("N"));
        _roots.Add(scratch);

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(scratch, "features", "features.json"));
        Assert.Null(features.SetEnabled(DevbookFeatures.C4Diagrams, true));
        Assert.Null(features.SetEnabled(DevbookFeatures.C4Zoom, true));

        var gitHub = new GitHubSettingsStore(Path.Combine(scratch, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        var repository = Assert.Single(repositories) with { CloneDirectory = clone };
        Assert.Null(gitHub.SetRepositories([repository]));

        var workspace = new WorkspaceSettingsStore(Path.Combine(scratch, "store"));
        return (features, gitHub, new DevbookFolderSource(gitHub, workspace), workspace, repository.Alias);
    }

    private async Task<C4Catalog> LoadAsync(string clone)
    {
        var (features, _, folders, _, alias) = Sources(clone);
        using var store = new C4DevbookStore(features, folders);
        return await store.LoadAsync(alias);
    }

    // ---- the catalog and the reader ----------------------------------------------------

    [Fact]
    public async Task A_component_whose_building_block_names_code_has_a_code_level()
    {
        var clone = CreateRepository();
        var catalog = await LoadAsync(clone);
        var code = new C4DevbookCode(new CSharpCodeReader(), new RecordingLauncher()).For(catalog);

        Assert.Equal(Path.GetFullPath(clone), Path.GetFullPath(catalog.RepositoryRoot!));
        Assert.True(code.Has(Devbook));
        Assert.False(code.Has("backlog.desktop.shell"));
        Assert.False(code.Has("backlog.desktop"));
    }

    [Fact]
    public async Task Without_a_building_block_no_component_has_a_code_level()
    {
        var catalog = await LoadAsync(CreateRepository(withBuildingBlock: false));
        var code = new C4DevbookCode(new CSharpCodeReader(), new RecordingLauncher()).For(catalog);

        Assert.False(code.Has(Devbook));
    }

    [Fact]
    public async Task The_level_is_the_folders_types_with_their_members_uses_and_the_components_decisions()
    {
        var catalog = await LoadAsync(CreateRepository());
        var code = new C4DevbookCode(new CSharpCodeReader(), new RecordingLauncher()).For(catalog);

        var level = await code.Load(Devbook, TestContext.Current.CancellationToken);

        Assert.Null(level.Problem);
        Assert.Equal("Devbook", level.Component);
        Assert.Equal(["src/Devbook"], level.Folders);

        var refresher = level.Types.Single(type => type.Name == "Refresher");
        Assert.Equal("«sealed class» : System.IDisposable", refresher.Stereotype);
        Assert.Equal(["src/Devbook/Refresher.cs"], refresher.FilePaths);
        Assert.Contains("+ Request(string root) : void", refresher.PublicMembers);
        Assert.DoesNotContain(refresher.PublicMembers, member => member.Contains("_builder", StringComparison.Ordinal));
        Assert.Contains(refresher.AllMembers, member => member.StartsWith("- _builder", StringComparison.Ordinal));

        Assert.Equal("«internal sealed class»", level.Types.Single(type => type.Name == "Builder").Stereotype);
        Assert.Contains(new C4CodeUse(refresher.Id, "Shop.Devbook.Builder", false), level.Uses);

        var decision = Assert.Single(level.Decisions);
        Assert.Equal("0004", decision.Number);
        Assert.Equal("Knowledge index is a generated database", decision.Title);
        Assert.EndsWith("adr/0004-knowledge-index.md", decision.Path, StringComparison.Ordinal);
    }

    /// <summary>Scenario "The source changed": a type added since the last opening is
    /// in the next one.</summary>
    [Fact]
    public async Task Opening_the_level_again_reads_the_source_as_it_is_now()
    {
        var clone = CreateRepository();
        var catalog = await LoadAsync(clone);
        var code = new C4DevbookCode(new CSharpCodeReader(), new RecordingLauncher()).For(catalog);
        Assert.Equal(2, (await code.Load(Devbook, TestContext.Current.CancellationToken)).Types.Count);

        File.WriteAllText(Path.Combine(clone, "src", "Devbook", "Added.cs"), "namespace Shop.Devbook; public record Added(int Value);");

        Assert.Contains((await code.Load(Devbook, TestContext.Current.CancellationToken)).Types, type => type.Name == "Added");
    }

    [Fact]
    public async Task Open_in_editor_hands_the_launcher_the_files_full_path_and_reports_its_failure()
    {
        var clone = CreateRepository();
        var launcher = new RecordingLauncher();
        var code = new C4DevbookCode(new CSharpCodeReader(), launcher).For(await LoadAsync(clone));

        Assert.Null(await code.OpenInEditor!("src/Devbook/Refresher.cs"));
        Assert.Equal([Path.Combine(Path.GetFullPath(clone), "src", "Devbook", "Refresher.cs")], launcher.Opened);

        Assert.Contains("not inside the repository", await code.OpenInEditor("../elsewhere.cs"), StringComparison.Ordinal);

        launcher.Failure = "VS Code is not installed.";
        Assert.Equal("VS Code is not installed.", await code.OpenInEditor("src/Devbook/Builder.cs"));
    }

    // ---- what the panel shows ---------------------------------------------------------

    /// <summary>
    /// End to end through the architecture panel: zooming into Devbook on the zoom tab
    /// opens its Code level read from the fixture's source, its decision opens the
    /// record's chapter, and Open in editor reaches the host's launcher.
    /// </summary>
    [Fact]
    public async Task The_surface_opens_the_code_level_of_a_component_with_code()
    {
        var clone = CreateRepository();
        var (features, gitHub, folders, workspace, alias) = Sources(clone);
        var launcher = new RecordingLauncher();
        var asked = new List<DevbookChapterLink>();

        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogC4Zoom.attach", _ => true).SetResult(true);
        context.Services.AddSingleton(workspace);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<C4DevbookStore>();
        context.Services.AddSingleton<IFolderEditorLauncher>(launcher);
        context.Services.AddSingleton<CSharpCodeReader>();
        context.Services.AddSingleton<C4DevbookCode>();

        var panel = context.Render<Arc42DevbookPanel>(parameters => parameters
            .Add(component => component.RepositoryAlias, alias)
            .Add(component => component.SelectedPath, (string?)null)
            .Add(component => component.OnNavigateToChapter, EventCallback.Factory.Create<DevbookChapterLink>(this, asked.Add)));
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("[data-testid='arc42-loading']")));

        // The surface lists the component's Code level among its views.
        Assert.Contains(panel.FindAll("[data-testid='arc42-surface-view']"), line => line.GetAttribute("data-code") == Devbook);

        var explorer = panel.FindComponent<C4ZoomExplorer>();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(C4MermaidWriter.AliasOf("backlog"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(C4MermaidWriter.AliasOf("backlog.desktop"), "containers-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(C4MermaidWriter.AliasOf(Devbook), "components-desktop"));

        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("[data-testid='c4-code-type']").Count), TimeSpan.FromSeconds(10));

        Assert.Contains("Code — Devbook component", panel.Find("[data-testid='c4-code-title']").TextContent, StringComparison.Ordinal);
        Assert.Contains("src/Devbook", panel.Find("[data-testid='c4-code-source']").TextContent, StringComparison.Ordinal);

        await panel.Find("[data-testid='c4-code-open']").ClickAsync(new());
        Assert.Single(launcher.Opened);

        await panel.Find("[data-testid='c4-code-decision']").ClickAsync(new());
        Assert.Contains(asked, link => link.Path.EndsWith("adr/0004-knowledge-index.md", StringComparison.Ordinal));
    }

    /// <summary>The words a class box and the type panel print: the kind with its
    /// modifiers, UML's visibility marks, and a record's number apart from its title.</summary>
    [Fact]
    public void Mapping_reads_kinds_marks_and_record_numbers_the_way_the_panel_prints_them()
    {
        Assert.Equal("~ Build() : bool", C4DevbookCode.Line(new CodeMember("Build", CodeMemberKind.Method, CodeAccessibility.Internal, [], "Build() : bool", "a.cs")));
        Assert.Equal("# Run() : void", C4DevbookCode.Line(new CodeMember("Run", CodeMemberKind.Method, CodeAccessibility.Protected, [], "Run() : void", "a.cs")));
        Assert.Equal("- _x : int", C4DevbookCode.Line(new CodeMember("_x", CodeMemberKind.Field, CodeAccessibility.Private, [], "_x : int", "a.cs")));

        var type = new CodeType("N.Shape", "Shape", "N", null, CodeTypeKind.RecordStruct, CodeAccessibility.Public, ["public", "readonly"], ["IShape", "IEquatable<Shape>"], ["a.cs"], []);
        Assert.Equal("readonly record struct", C4DevbookCode.KindOf(type));
        Assert.Equal("«readonly record struct» : IShape, IEquatable<Shape>", C4DevbookCode.Stereotype(type));

        var nested = type with { Kind = CodeTypeKind.Class, Accessibility = CodeAccessibility.PrivateProtected, Modifiers = ["private", "protected", "static"], BaseList = [] };
        Assert.Equal("«private protected static class»", C4DevbookCode.Stereotype(nested));

        var record = C4DevbookCode.Record(new C4LinkedRecord(".devbook/arc42/adr/0015-store.md", "ADR 0015: The devbook database lives in app storage", C4RecordKind.Decision, null, null, []));
        Assert.Equal(("0015", "The devbook database lives in app storage"), (record.Number, record.Title));

        var untitled = C4DevbookCode.Record(new C4LinkedRecord("notes/decision.md", "Use SQLite", C4RecordKind.Decision, null, null, []));
        Assert.Equal((string.Empty, "Use SQLite"), (untitled.Number, untitled.Title));
    }

    private sealed class RecordingLauncher : IFolderEditorLauncher
    {
        public List<string> Opened { get; } = [];

        public string? Failure { get; set; }

        public Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw new FolderEditorLaunchException(Failure);

            Opened.Add(filePath);
            return Task.CompletedTask;
        }
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
