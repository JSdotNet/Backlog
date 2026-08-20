using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using AngleSharp.Dom;

using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository identity mark on the header's scope filter, and the header control
/// that decides whether the mark is drawn at all.
/// <para>
/// Classes rather than colours, because the shell says <em>which</em> repository a chip
/// is for and the stylesheet says which hue that is. What matters here is that the
/// shell reads the same answer the entry list and the roadmap read — which is the whole
/// reason the choice lives in Settings rather than on any one of them.
/// </para>
/// <para>
/// Every test that expects a mark turns the visualization on first, because off is what
/// a fresh workspace gets. That is not scaffolding around an awkward default — it is the
/// feature: the marks are an opt-in layer, so a test that wants one has to say so.
/// </para>
/// </summary>
public sealed class HomeRepositoryScopeTests
{
    [Fact]
    public void Each_scope_chip_carries_its_repositorys_mark()
    {
        using var harness = CreateHarness(ShowingColours);
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // Position, because neither repository has been given a colour of its own.
        Assert.Contains("repo-mark--1", chips[0].ClassName);
        Assert.Contains("repo-mark--2", chips[1].ClassName);
    }

    [Fact]
    public void A_chosen_colour_reaches_the_chip()
    {
        using var harness = CreateHarness(settings =>
        {
            ShowingColours(settings);
            Assert.Null(settings.SetRepositoryColour("backlog", 5));
        });
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        Assert.Contains("repo-mark--5", chips[0].ClassName);
    }

    [Fact]
    public void The_mark_is_never_the_only_thing_saying_which_repository()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // .design/color-scheme.md#band-identity-tokens requires it: the alias is written
        // on the chip, so a reader who never sees a hue loses nothing.
        Assert.Equal("backlog", chips[0].TextContent.Trim());
        Assert.Equal("docs", chips[1].TextContent.Trim());
    }

    [Fact]
    public void Selecting_a_chip_keeps_its_mark()
    {
        using var harness = CreateHarness(ShowingColours);
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");
        chips[0].Click();

        // The mark is identity and the fill is selection. They are two different facts
        // and the chip has to be able to carry both at once, which is why the identity
        // rule is an edge rather than a surface.
        component.WaitForAssertion(() =>
        {
            var selected = component.FindAll("[data-testid='repository-filter-option']")[0];
            Assert.Contains("chip--active", selected.ClassName);
            Assert.Contains("repo-mark--1", selected.ClassName);
        });
    }

    // --- The repository colour visualization ----------------------------------
    //
    // The switch is here, on the main page, beside the chips it is most visibly about.
    // What it flips is not a shell concern though — it is the settings store's answer to
    // "which hue", which is why turning it off has to empty every surface at once rather
    // than only this one.
    //
    // Asserted on the switch inside the control rather than on the control itself: the
    // shared Toggle puts the test id on the wrapper that holds the track and the label
    // together, because the label is part of the control and a test id on the button
    // alone would name only half of it.

    [Fact]
    public void The_header_offers_the_colour_visualization_switch_beside_the_scope_chips()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var control = component.WaitForElement("[data-testid='repository-colours-toggle']");
        var switchElement = ColourSwitch(component);

        // A switch and not a disclosure or a selection: nothing opens, nothing is picked
        // out of a set, a fact about the whole workspace changes. So role=switch with
        // aria-checked, and no aria-expanded to suggest otherwise.
        Assert.Equal("switch", switchElement.GetAttribute("role"));
        Assert.Equal("false", switchElement.GetAttribute("aria-checked"));
        Assert.Null(switchElement.GetAttribute("aria-expanded"));

        // Named by the text beside it rather than by an aria-label over the top of it,
        // so the accessible name and the visible one cannot drift apart.
        Assert.Null(switchElement.GetAttribute("aria-label"));
        var labelId = switchElement.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrWhiteSpace(labelId));
        Assert.Equal("Repository colours", component.Find("#" + labelId).TextContent.Trim());
        Assert.Contains("Repository colours", control.TextContent);
    }

    [Fact]
    public void The_switch_is_a_real_button_so_the_keyboard_already_works()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var switchElement = ColourSwitch(component);

        // Enter and Space are the browser's, not ours — which is the whole reason the
        // library draws a switch as a button rather than as a styled div. Asserting the
        // element is the honest version of that test; simulating the two keys would be a
        // test of the renderer rather than of anything this screen decides.
        Assert.Equal("BUTTON", switchElement.TagName);
        Assert.Equal("button", switchElement.GetAttribute("type"));
        Assert.False(switchElement.HasAttribute("disabled"));
    }

    [Fact]
    public void No_chip_carries_a_mark_until_the_visualization_is_turned_on()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // Off is the first-run answer, and off means the chip's ordinary presentation —
        // the one a repository with nothing configured already had — rather than a
        // "colours off" style invented for the occasion.
        Assert.All(chips, chip => Assert.DoesNotContain("repo-mark", chip.ClassName));
    }

    [Fact]
    public void Turning_the_visualization_on_marks_every_chip()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ColourSwitch(component).Click();

        component.WaitForAssertion(() =>
        {
            var chips = component.FindAll("[data-testid='repository-filter-option']");
            Assert.Contains("repo-mark--1", chips[0].ClassName);
            Assert.Contains("repo-mark--2", chips[1].ClassName);
            Assert.Equal("true", ColourSwitch(component).GetAttribute("aria-checked"));
        });
    }

    [Fact]
    public void The_toggle_records_the_choice_where_every_surface_reads_it()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ColourSwitch(component).Click();

        // Not a field on the shell. The point of pressing it here is that the entry list,
        // the sessions pane and the roadmap band read the same answer — and that the
        // answer is on disk before the next launch asks for it.
        component.WaitForAssertion(() => Assert.True(harness.GitHubSettings.Current.ShowRepositoryColours));
        Assert.True(new GitHubSettingsStore(harness.GitHubSettings.SettingsPath).Current.ShowRepositoryColours);
    }

    [Fact]
    public void Turning_the_visualization_off_again_takes_the_marks_back_off()
    {
        using var harness = CreateHarness(ShowingColours);
        var component = Render(harness);

        Assert.Equal("true", ColourSwitch(component).GetAttribute("aria-checked"));

        ColourSwitch(component).Click();

        component.WaitForAssertion(() => Assert.All(
            component.FindAll("[data-testid='repository-filter-option']"),
            chip => Assert.DoesNotContain("repo-mark", chip.ClassName)));

        Assert.Equal("false", ColourSwitch(component).GetAttribute("aria-checked"));

        // Hiding the layer does not unmake the choice underneath it — Settings' swatches
        // still have something to show.
        Assert.Equal(1, harness.GitHubSettings.Current.ColourFor("backlog"));
    }

    [Fact]
    public void The_sessions_pane_is_handed_the_gated_answer_too()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForElement("[data-testid='sessions-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']")));

        // The pane is handed a function rather than a dictionary, so the gate has to be
        // inside the function the shell passes down — the pane itself knows nothing about
        // a visualization and should not have to.
        var colourFor = component.FindComponent<SessionsPane>().Instance.RepositoryColour;
        Assert.NotNull(colourFor);
        Assert.Null(colourFor("JSdotNet/Backlog"));

        ColourSwitch(component).Click();

        component.WaitForAssertion(() => Assert.Equal(
            1,
            component.FindComponent<SessionsPane>().Instance.RepositoryColour!("JSdotNet/Backlog")));
    }

    /// <summary>The switch inside the header's colour control. Found through the
    /// control's test id rather than by its own, because that is where the shared Toggle
    /// puts one — see the section comment above.</summary>
    private static IElement ColourSwitch(IRenderedComponent<Home> component) =>
        component.WaitForElement("[data-testid='repository-colours-toggle'] [role='switch']");

    /// <summary>Turns the identity-hue visualization on, the way somebody using the
    /// header switch would have before this screen was opened.</summary>
    private static void ShowingColours(GitHubSettingsStore settings) =>
        Assert.Null(settings.SetShowRepositoryColours(true));

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    private static Harness CreateHarness(Action<GitHubSettingsStore>? configureRepositories = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-repository-scope-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        // The three surfaces under test are on; everything that would put extra
        // chrome or a network call in the way is off.
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, true);
        _ = featureSettings.SetEnabled(DashboardFeatures.Dashboard, true);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, true);
        _ = featureSettings.SetEnabled(SessionFeatures.Sessions, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);
        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, false);
        _ = featureSettings.SetEnabled(AppFeatures.AiAssistant, false);
        _ = featureSettings.SetEnabled(AppFeatures.FeedbackReporting, false);
        _ = featureSettings.SetEnabled(BacklogFeatures.GitHubIntegration, false);

        var (repositories, errors) = GitHubSettings.ParseText(
            "backlog = JSdotNet/Backlog" + Environment.NewLine + "docs = JSdotNet/Docs");
        Assert.Empty(errors);

        Assert.Null(gitHubSettings.SetRepositories(
        [
            .. repositories.Select(repository => repository with
            {
                CloneDirectory = RepositoryRoot.Root.FullName,
                KnowledgeFolders = KnowledgeFolderSetting.Defaults()
            })
        ]));

        configureRepositories?.Invoke(gitHubSettings);

        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());
        var knowledgeFolderSource = new KnowledgeFolderSource(gitHubSettings, store);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<ICopilotToolService, UnsupportedCopilotToolService>();

        // The sessions takeover, with nothing on the machine behind it. A shell test
        // asking whether the takeover replaced the panes should not also be reading
        // whatever the person running it had been doing that morning — and a pane that
        // only worked with rows in it would fail here, which is the point.
        context.Services.AddSingleton<IAgentSessionSource>(new EmptySessionSource());
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(knowledgeFolderSource);
        // The Roadmap module the way a host wires it: a real plan document under the
        // same storage root, so the band draws what was stored rather than a fixture.
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            BacklogTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        // The band gathers an item's linked and tagged work through this port before it
        // opens the editor, so a host that composes the band composes the rollup with it.
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                BacklogTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        // The dashboard takeover, with no provider behind it — see DashboardTestHost.
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddScoped(sp => BacklogTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            BacklogCopilotCli.Unavailable));

        return new Harness(root, context, gitHubSettings);
    }

    private sealed record Harness(string Root, BunitContext Context, GitHubSettingsStore GitHubSettings) : IDisposable
    {
        public void Dispose()
        {
            Context.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class EmptySessionSource : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentSessionCatalog.Empty);
    }

    private sealed class StubAzureFoundryChatClient : IAzureFoundryChatClient
    {
        public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryChatResponse("Not used in this test."));
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
