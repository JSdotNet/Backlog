using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository chapter viewer's Edit control and status display, each behind
/// a switch of its own (<see cref="DevbookFeatures.Editing"/>,
/// <see cref="DevbookFeatures.Status"/>). Every panel that offers them is
/// rendered with the switch on and with it off, over a clone it could otherwise
/// write to, so a missing control proves the switch and not a chapter that failed
/// to render.
/// </summary>
public sealed class DevbookPanelsViewerSwitchesTests : IDisposable
{
    private const string Alias = "backlog";

    private readonly List<string> _tempDirs = [];

    public static TheoryData<string> Panels => ["domain", "design", "instructions", "arc42"];

    // --- Edit ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Panels))]
    public void Edit_is_offered_while_devbook_editing_is_on(string panel)
    {
        using var harness = Harness(editing: true, status: true);

        var component = RenderPanel(harness, panel);

        component.WaitForAssertion(() => Assert.Single(component.FindAll(EditSelector(panel))));
    }

    [Theory]
    [MemberData(nameof(Panels))]
    public void Edit_is_not_offered_while_devbook_editing_is_off(string panel)
    {
        using var harness = Harness(editing: false, status: true);

        var component = RenderPanel(harness, panel);

        component.WaitForAssertion(() => Assert.Contains(Prose(panel), component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll(EditSelector(panel)));
    }

    /// <summary>Switching Edit off under an open editor leaves edit mode through
    /// the panel's own path, which writes what was typed before the editor
    /// goes.</summary>
    [Fact]
    public void Switching_editing_off_closes_an_open_editor_and_keeps_what_was_typed()
    {
        using var harness = Harness(editing: true, status: true);

        var component = RenderPanel(harness, "domain");
        component.WaitForAssertion(() => Assert.Single(component.FindAll(EditSelector("domain"))));
        component.Find(EditSelector("domain")).Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("textarea")));
        component.Find("textarea").Input("# Context Map\n\nTyped before the switch.\n");

        harness.Features.SetEnabled(DevbookFeatures.Editing, false);

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("textarea")));
        Assert.Empty(component.FindAll(EditSelector("domain")));
        Assert.Contains("Typed before the switch.", File.ReadAllText(Path.Combine(harness.Root, ".domain", "context-map.md")), StringComparison.Ordinal);
    }

    // --- Status ----------------------------------------------------------------

    [Theory]
    [InlineData("domain")]
    [InlineData("design")]
    [InlineData("arc42")]
    public void Status_is_shown_while_devbook_status_is_on(string panel)
    {
        using var harness = Harness(editing: false, status: true);

        var component = RenderPanel(harness, panel);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(StatusSelector)));
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("design")]
    [InlineData("arc42")]
    public void Status_is_not_shown_while_devbook_status_is_off(string panel)
    {
        using var harness = Harness(editing: false, status: false);

        var component = RenderPanel(harness, panel);

        component.WaitForAssertion(() => Assert.Contains(Prose(panel), component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll(StatusSelector));
        Assert.Empty(component.FindAll(".devbook-record__state"));

        // The headings the status sat beside are still there.
        Assert.Contains(Heading(panel), component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Switching_status_on_shows_it_without_reopening_the_panel()
    {
        using var harness = Harness(editing: false, status: false);

        var component = RenderPanel(harness, "domain");
        component.WaitForAssertion(() => Assert.Contains(Prose("domain"), component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll(StatusSelector));

        harness.Features.SetEnabled(DevbookFeatures.Status, true);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(StatusSelector)));
    }

    // --- Fixtures --------------------------------------------------------------

    /// <summary>Every way a status is drawn in these panels: the select, the
    /// badge, and the Domain panel's own action-strip forms of both.</summary>
    private const string StatusSelector =
        ".status-editor, .badge--status, [data-testid='devbook-state-select'], [data-testid='devbook-state-badge']";

    private static string EditSelector(string panel) => panel switch
    {
        "instructions" => "[data-testid='instructions-document-edit']",
        _ => $"[data-testid='{panel}-chapter-file-edit']"
    };

    private static string Prose(string panel) => panel switch
    {
        "domain" => "Original prose.",
        "design" => "Palette prose.",
        "instructions" => "Project instructions.",
        _ => "Introduction prose."
    };

    private static string Heading(string panel) => panel switch
    {
        "domain" => "Boundaries",
        "design" => "Primary",
        _ => "Goals"
    };

    private static IRenderedComponent<ContainerFragment> RenderPanel(TestHarness harness, string panel) => panel switch
    {
        "domain" => Render<DomainDevbookPanel>(harness, ".domain/context-map.md"),
        "design" => Render<DesignDevbookView>(harness, "colors.md"),
        "instructions" => Render<InstructionsDevbookPanel>(harness, "CLAUDE.md"),
        _ => Render<Arc42DevbookPanel>(harness, ".arc42/01-introduction.md")
    };

    /// <summary>Rendered as a fragment so the four panels come back as one type:
    /// each takes the same two parameters under the same names.</summary>
    private static IRenderedComponent<ContainerFragment> Render<TPanel>(TestHarness harness, string selectedPath)
        where TPanel : IComponent =>
        harness.Context.Render(builder =>
        {
            builder.OpenComponent<TPanel>(0);
            builder.AddAttribute(1, "RepositoryAlias", Alias);
            builder.AddAttribute(2, "SelectedPath", selectedPath);
            builder.CloseComponent();
        });

    private TestHarness Harness(bool editing, bool status)
    {
        var root = TempDir();
        Write(root, ".domain/context-map.md",
            "# Context Map\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n\n## Boundaries\n\n```meta\nstatus: draft\n```\n\nWhat divides the contexts.\n");
        Write(root, ".design/colors.md",
            "# Colors\n\n```meta\nstatus: draft\n```\n\nPalette prose.\n\n## Primary\n\n```meta\nstatus: draft\n```\n\nThe main colour.\n");
        Write(root, ".arc42/01-introduction.md",
            "# Introduction\n\n```meta\nstatus: draft\n```\n\nIntroduction prose.\n\n## Goals\n\n```meta\nstatus: draft\n```\n\nWhat it is for.\n");
        Write(root, "CLAUDE.md", "# Claude\n\nProject instructions.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHub.SetRepositories([Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders = DevbookFolderSetting.Defaults()
        }]));

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        Assert.Null(features.SetEnabled(DevbookFeatures.RepositoryDevbook, true));
        Assert.Null(features.SetEnabled(DevbookFeatures.Editing, editing));
        Assert.Null(features.SetEnabled(DevbookFeatures.Status, status));

        var folders = new DevbookFolderSource(gitHub, settings);

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton(new DomainDevbookStore(folders));
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());
        context.Services.AddSingleton<DesignDevbookProvider>();
        context.Services.AddSingleton<AiDevbookProvider>();
        context.Services.AddSingleton(new InstructionSourceDiscovery());

        return new TestHarness(root, context, features);
    }

    private static void Write(string root, string relativePath, string text)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "devbook-panels-viewer-switches-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed record TestHarness(string Root, BunitContext Context, AppFeatureSettingsStore Features) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }
}
