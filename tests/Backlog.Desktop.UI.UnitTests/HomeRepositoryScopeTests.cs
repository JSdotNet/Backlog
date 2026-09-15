using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using AngleSharp.Dom;

using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
    /// <summary>
    /// The identity region opens on the scope chips, because there is nothing in
    /// front of them any more.
    /// <para>
    /// It used to open with a "Backlog" wordmark. The window border carries the name,
    /// so the wordmark was the app saying twice which app you were in and spending
    /// the width of the word on it — and it left the shell owning an <c>h1</c> that
    /// was chrome rather than the page's title, which is a heading every other screen
    /// supplies for itself.
    /// </para>
    /// </summary>
    [Fact]
    public void The_header_carries_no_wordmark()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Assert.Empty(component.FindAll(".app-header h1"));

        // And the region is still there, still holding the scope: the wordmark went,
        // not the region around it.
        Assert.NotNull(component.Find(".app-header__identity [aria-label='Repository scope']"));
    }

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

    // --- More than one repository at once ----------------------------------------
    //
    // A plain press is the single select the strip always had. With Ctrl (Cmd on a
    // Mac) held it adds or removes one, and the first one taken is the anchor — the
    // repository the knowledge pane reads, since the pane can read one and not
    // several. The anchor is marked only once there is a second chip for it to be
    // distinct from; with one chip pressed the markup is what it was before.

    [Fact]
    public void A_plain_press_scopes_to_one_repository_and_a_second_plain_press_replaces_it()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Assert.Equal(["backlog"], state.SelectedRepositoryAliases);

        Chips(component)[1].Click();
        Assert.Equal(["docs"], state.SelectedRepositoryAliases);

        // And pressing the one that is alone in the scope clears it, as it always did.
        Chips(component)[1].Click();
        Assert.Empty(state.SelectedRepositoryAliases);
    }

    [Fact]
    public void A_modified_press_adds_a_repository_beside_the_first()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["backlog", "docs"], state.SelectedRepositoryAliases);
        component.WaitForAssertion(() =>
            Assert.All(Chips(component), chip => Assert.Equal("true", chip.GetAttribute("aria-pressed"))));
    }

    [Fact]
    public void Cmd_counts_as_the_modifier_too()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Chips(component)[1].Click(new MouseEventArgs { MetaKey = true });

        Assert.Equal(["backlog", "docs"], state.SelectedRepositoryAliases);
    }

    [Fact]
    public void A_modified_press_on_a_scoped_chip_takes_it_back_out()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["backlog"], state.SelectedRepositoryAliases);
    }

    [Fact]
    public void The_knowledge_pane_follows_the_anchor_not_the_latest_press()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Chips(component)[1].Click();

        // Pinned, so opening Knowledge puts it beside the list rather than in its
        // place — a pane press is a switch, and the list has to stay for a second
        // repository to have anywhere to be.
        component.WaitForElement("[data-testid='backlog-pane-pin']").Click();
        component.WaitForElement("[data-testid='knowledge-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("docs", component.FindComponent<KnowledgePane>().Instance.RepositoryAlias);
        });

        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });

        // Two repositories in the list, and the pane still reading the first one taken:
        // adding a repository to the backlog scope must not move the knowledge the reader
        // was already reading beside it.
        component.WaitForAssertion(() =>
            Assert.Equal("docs", component.FindComponent<KnowledgePane>().Instance.RepositoryAlias));
    }

    [Fact]
    public void The_anchor_is_marked_only_once_there_is_a_second_chip_to_tell_it_from()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Chips(component)[1].Click();

        // One chip pressed: nothing to be the anchor of, so no mark — the markup
        // stays what it was before the strip could hold two.
        component.WaitForAssertion(() =>
        {
            var chips = Chips(component);
            Assert.All(chips, chip => Assert.Null(chip.GetAttribute("aria-current")));
            Assert.All(chips, chip => Assert.DoesNotContain("chip--anchor", chip.ClassName));
        });

        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            var chips = Chips(component);
            Assert.Equal("true", chips[1].GetAttribute("aria-current"));
            Assert.Contains("chip--anchor", chips[1].ClassName);
            Assert.Null(chips[0].GetAttribute("aria-current"));
            Assert.DoesNotContain("chip--anchor", chips[0].ClassName);
        });
    }

    [Fact]
    public void Removing_the_anchor_hands_the_mark_to_the_next_repository()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[1].Click();
        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["backlog"], state.SelectedRepositoryAliases);
        Assert.Equal("backlog", state.AnchorRepositoryAlias);
    }

    [Fact]
    public void The_chip_says_how_to_add_one()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = Chips(component);

        // The modifier is the one thing about the strip nothing on screen shows, so
        // the chip's own tooltip is where it is written down.
        Assert.Contains("Ctrl+click", chips[0].GetAttribute("title"));
    }

    // --- Several only while the list is on screen ------------------------------------
    //
    // More than one repository is a list affordance: the backlog list is the only
    // pane that can show several. So the scope holds several only while Tasks is on
    // screen — going to Knowledge, which as an unpinned switch takes the list's
    // place, narrows the scope to the anchor, and without the list a modified press
    // is an ordinary press.

    [Fact]
    public void Going_to_knowledge_in_place_of_the_list_narrows_the_scope_to_the_anchor()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[1].Click();
        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });
        Assert.Equal(["docs", "backlog"], state.SelectedRepositoryAliases);

        GoToKnowledge(component);

        // The anchor, not the latest: docs was taken first, and it is what the
        // knowledge pane would have been reading beside the list.
        component.WaitForAssertion(() => Assert.Equal(["docs"], state.SelectedRepositoryAliases));
        Assert.Equal("docs", component.FindComponent<KnowledgePane>().Instance.RepositoryAlias);
    }

    [Fact]
    public void Without_the_list_a_modified_press_is_an_ordinary_press()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        GoToKnowledge(component);

        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["docs"], state.SelectedRepositoryAliases);
        Assert.DoesNotContain("Ctrl+click", Chips(component)[0].GetAttribute("title"));
    }

    /// <summary>Presses the Knowledge option, which — Tasks being unpinned — puts
    /// the knowledge pane where the list was.</summary>
    private static void GoToKnowledge(IRenderedComponent<Home> component)
    {
        component.WaitForElement("[data-testid='knowledge-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-stack']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    private static IReadOnlyList<IElement> Chips(IRenderedComponent<Home> component) =>
        component.WaitForElements("[data-testid='repository-filter-option']");

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
        Assert.Equal("Colors", component.Find("#" + labelId).TextContent.Trim());
        Assert.Contains("Colors", control.TextContent);
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
        _ = featureSettings.SetEnabled(TasksFeatures.GitHubIntegration, false);

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
        context.Services.AddSingleton(new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json")));
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<IDevToolService, UnsupportedDevToolService>();

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
            TasksTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        // The band gathers an item's linked and tagged work through this port before it
        // opens the editor, so a host that composes the band composes the rollup with it.
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                TasksTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<KnowledgeUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<KnowledgeSourceSelection>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        // The dashboard takeover, with no provider behind it — see DashboardTestHost.
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable,
            toasts: sp.GetRequiredService<IToastChannel>()));
        // Home answers the Inbox's Capture button through the module's runner and
        // injects it hard, so a host that renders Home composes the module and
        // picks where its sources are kept, the same as the application hosts do.
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddCaptureModule();
        InboxTestHost.AddCaptureDelivery(context.Services);

        TasksTestHost.AddToastChannel(context.Services);
        // The Inbox pane the shell now composes: its state over the in-memory
        // module, the same terms as the Tasks state above.
        _ = InboxTestHost.AddInboxState(context.Services);

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

        public Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryPlanResponse("# Not used in this test."));
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

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
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
