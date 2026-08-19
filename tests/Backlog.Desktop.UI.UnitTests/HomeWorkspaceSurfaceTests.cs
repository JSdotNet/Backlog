using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shell shows one surface at a time. Tools and the Dashboard are whole
/// domains rather than panes: opening either takes the screen and hides every
/// other domain concern — the roadmap band included — and closing it puts the
/// reader back exactly where they were.
/// <para>
/// That last part is worth a test even though no code implements it. The surface
/// is a field of its own and the pane selection is never touched to open one, so
/// the selection is simply revealed again rather than saved and restored. A future
/// change that folds the surfaces into <c>GlobalPaneSelection</c> would pass every
/// markup assertion and quietly break this.
/// </para>
/// </summary>
public sealed class HomeWorkspaceSurfaceTests
{
    [Fact]
    public void Opening_tools_hides_the_roadmap_band_and_every_pane()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.Find("[data-testid='tools-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='tools-panel']"));
            Assert.Empty(component.FindAll("[data-testid='knowledge-layout']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    [Fact]
    public void Opening_the_dashboard_hides_the_roadmap_band_and_every_pane()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Empty(component.FindAll("[data-testid='knowledge-layout']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    /// <summary>
    /// One field with three states, so opening the second takeover is the same act
    /// as closing the first. There is no arrangement in which both are on screen.
    /// </summary>
    [Fact]
    public void The_two_surfaces_cannot_be_open_at_the_same_time()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']")));

        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.Empty(component.FindAll("[data-testid='tools-surface']"));
        });

        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-surface']"));
        });
    }

    /// <summary>
    /// A non-default pane selection has to survive a takeover. Knowledge is turned
    /// on first precisely so the assertion is about the reader's choice rather than
    /// about the default the shell would fall back to anyway.
    /// </summary>
    [Fact]
    public void Closing_a_surface_restores_the_workspace_with_the_pane_selection_unchanged()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='knowledge-pane-option']")));
        component.Find("[data-testid='knowledge-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-stack']"));
        });

        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.Empty(component.FindAll("[data-testid='knowledge-stack']"));
        });

        // The in-surface ✕ and the header toggle both close it; this is the ✕.
        component.Find("[data-testid='tools-panel'] button.btn--ghost").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='tools-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-layout']"));

            // Both panes are back, which is only true because opening the surface
            // never disabled either of them.
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-stack']"));
        });
    }

    [Fact]
    public void The_roadmap_band_renders_above_the_panes_when_its_feature_is_on()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var workspace = component.Find("[data-testid='workspace']");
            Assert.DoesNotContain("workspace--no-roadmap", workspace.GetAttribute("class"));

            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-layout']"));
        });

        // The band above, the panes below — the order the layout depends on.
        var bandAt = component.Markup.IndexOf("data-testid=\"roadmap-band\"", StringComparison.Ordinal);
        var panesAt = component.Markup.IndexOf("data-testid=\"knowledge-layout\"", StringComparison.Ordinal);

        Assert.True(bandAt >= 0 && panesAt > bandAt);
    }

    [Fact]
    public void The_roadmap_band_is_absent_when_its_feature_is_off()
    {
        using var harness = CreateHarness(features => features.SetEnabled(RoadmapFeatures.Roadmap, false));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            // And no option offering one. The flag decides whether the option exists;
            // the option decides whether the band is on screen.
            Assert.Empty(component.FindAll("[data-testid='roadmap-pane-option']"));

            // The strip itself stays, because the panes still need it.
            Assert.NotEmpty(component.FindAll("[data-testid='global-pane-multiselect']"));

            // No band means no empty track left where it would have been.
            var workspace = component.Find("[data-testid='workspace']");
            Assert.Contains("workspace--no-roadmap", workspace.GetAttribute("class"));

            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-layout']"));
        });
    }

    [Fact]
    public void A_surface_whose_feature_is_switched_off_falls_back_to_the_workspace()
    {
        using var harness = CreateHarness(features => features.SetEnabled(MonitoringFeatures.Dashboard, false));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='dashboard-toggle-button']"));
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-layout']"));
        });
    }

    /// <summary>
    /// The band ships shown, and its header option says so. Nothing on the band
    /// itself controls it: the only affordance is the option in the header strip,
    /// pointed at the band's landmark through <c>aria-controls</c>, so there is one
    /// affordance for one thing rather than a header option and a chevron competing.
    /// </summary>
    [Fact]
    public void The_roadmap_band_starts_shown_and_its_header_option_is_pressed()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var option = component.Find("[data-testid='roadmap-pane-option']");

            Assert.Equal("true", option.GetAttribute("aria-pressed"));
            Assert.Equal("roadmap-band", option.GetAttribute("aria-controls"));
            Assert.Equal("Roadmap", option.TextContent.Trim());

            // No capacity rule, so unlike the three panes it is never blocked.
            Assert.False(option.HasAttribute("disabled"));

            // It sits in the same strip as the panes, which is what makes it read as
            // one of them, and first in it because the band is above them on screen.
            var strip = component.Find("[data-testid='global-pane-multiselect']");
            var options = strip.QuerySelectorAll("[data-testid$='-pane-option']");

            Assert.Equal("roadmap-pane-option", options[0].GetAttribute("data-testid"));

            var band = component.Find("[data-testid='roadmap-band']");

            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.Equal("Planning", band.QuerySelector(".roadmap-band__eyebrow")!.TextContent.Trim());
            Assert.Equal("Roadmap", component.Find("#roadmap-band-title").TextContent.Trim());
        });
    }

    /// <summary>
    /// Hiding is binary: the band leaves the DOM entirely and its grid track goes
    /// with it, through the same <c>workspace--no-roadmap</c> variant the feature flag
    /// uses. There is no smaller band left behind, so there is no second grid variant
    /// and nothing on screen to fold.
    /// </summary>
    [Fact]
    public void Turning_the_roadmap_option_off_removes_the_band_and_its_track()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']")));
        component.Find("[data-testid='roadmap-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band-empty-state']"));

            Assert.Contains("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));

            // The option stays, unpressed and enabled, because it is the way back.
            var option = component.Find("[data-testid='roadmap-pane-option']");

            Assert.Equal("false", option.GetAttribute("aria-pressed"));
            Assert.False(option.HasAttribute("disabled"));

            // The panes are untouched: the band was never competing with them.
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-layout']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    [Fact]
    public void Turning_the_roadmap_option_on_again_restores_the_band()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-pane-option']")));

        component.Find("[data-testid='roadmap-pane-option']").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='roadmap-band']")));

        component.Find("[data-testid='roadmap-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.Equal("true", component.Find("[data-testid='roadmap-pane-option']").GetAttribute("aria-pressed"));

            Assert.DoesNotContain("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });
    }

    /// <summary>
    /// A resize may not touch the band, and this is the guarantee that keeping it out
    /// of <see cref="GlobalPaneSelection"/> buys. That selection has a viewport-driven
    /// capacity and trims itself to fit; a band folded into it would be evictable by
    /// window width, which is a horizontal rule applied to a horizontal row that
    /// competes with nothing. The resize is driven through the shell's own capacity
    /// entry point — the path the window's resize listener calls from JavaScript —
    /// because that is the only thing a resize gets to say here.
    /// </summary>
    [Fact]
    public async Task A_window_resize_never_changes_whether_the_band_is_shown()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']")));

        // Shown: down to a single-pane window and back again.
        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(1));
        AssertShown(component);

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(3));
        AssertShown(component);

        // And hidden, which is the direction a capacity trim could not accidentally
        // get right: the band has to stay gone rather than reappearing on a resize.
        component.Find("[data-testid='roadmap-pane-option']").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='roadmap-band']")));

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(1));
        AssertHidden(component);

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(3));
        AssertHidden(component);

        static void AssertShown(IRenderedComponent<Home> rendered) =>
            rendered.WaitForAssertion(() =>
            {
                Assert.NotEmpty(rendered.FindAll("[data-testid='roadmap-band']"));
                Assert.Equal("true", rendered.Find("[data-testid='roadmap-pane-option']").GetAttribute("aria-pressed"));
                Assert.DoesNotContain("workspace--no-roadmap",
                    rendered.Find("[data-testid='workspace']").GetAttribute("class"));
            });

        static void AssertHidden(IRenderedComponent<Home> rendered) =>
            rendered.WaitForAssertion(() =>
            {
                Assert.Empty(rendered.FindAll("[data-testid='roadmap-band']"));
                Assert.Equal("false", rendered.Find("[data-testid='roadmap-pane-option']").GetAttribute("aria-pressed"));
                Assert.Contains("workspace--no-roadmap",
                    rendered.Find("[data-testid='workspace']").GetAttribute("class"));
            });
    }

    /// <summary>
    /// The band is a row above the panes, not a fourth pane, and nothing may quietly
    /// make it one. Folding it into <see cref="GlobalPane"/> would hand it a
    /// viewport-driven capacity and the "one is always on screen" invariant, neither
    /// of which is true of a horizontal band — a narrowed window would start evicting
    /// the reader's roadmap through <c>TrimToCapacity</c>. This is a tripwire on a
    /// later tidy-up rather than a test of behaviour, which is why it counts members
    /// rather than exercising anything.
    /// </summary>
    [Fact]
    public void The_global_pane_enum_still_describes_three_panes_and_no_band()
    {
        GlobalPane[] expected = [GlobalPane.Inbox, GlobalPane.Backlog, GlobalPane.Knowledge];

        Assert.Equal(expected, Enum.GetValues<GlobalPane>());
    }

    /// <summary>
    /// The fold survives a takeover, and this is the test that makes shell-owned
    /// visibility load-bearing rather than a preference.
    /// <para>
    /// A takeover does not hide the band, it removes it:
    /// <see cref="Opening_tools_hides_the_roadmap_band_and_every_pane"/> asserts the
    /// band is absent from the DOM, not merely off screen. So <c>RoadmapBand</c> is
    /// disposed on the way in and constructed afresh on the way out, and a private
    /// <c>bool</c> inside it would come back at its initializer — shown — on every
    /// round trip. Nothing in the markup would look wrong; the reader would just find
    /// the band back each time they came out of Tools, having hidden it. Holding the
    /// state on the shell, which outlives the band, is what prevents that, and this
    /// pins it.
    /// </para>
    /// </summary>
    [Fact]
    public void A_hidden_band_stays_hidden_across_a_takeover_round_trip()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-pane-option']")));
        component.Find("[data-testid='roadmap-pane-option']").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='roadmap-band']")));

        // In: the pane layout that would have held the band goes with it.
        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
        });

        // Out, through the header toggle rather than the in-surface ✕ — either closes
        // it, and the other round-trip test above covers the ✕.
        component.Find("[data-testid='tools-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='tools-surface']"));

            // The workspace is back and the band is not, drawn from state that was
            // never the band's own.
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("false", component.Find("[data-testid='roadmap-pane-option']").GetAttribute("aria-pressed"));

            Assert.Contains("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });
    }

    /// <summary>
    /// The feature flag and the header option are independent. The flag decides
    /// whether there is an option at all; the option decides whether the band is on
    /// screen. So switching the flag off leaves the option's state retained but
    /// unobservable, and switching it back on restores what the reader left rather
    /// than resetting it to shown.
    /// </summary>
    [Fact]
    public void The_bands_visibility_survives_its_feature_going_off_and_back_on()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var features = (AppFeatureSettingsStore)harness.Context.Services.GetRequiredService<IAppFeatureSettings>();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-pane-option']")));
        component.Find("[data-testid='roadmap-pane-option']").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='roadmap-band']")));

        _ = features.SetEnabled(RoadmapFeatures.Roadmap, false);

        component.WaitForAssertion(() =>
        {
            // The option goes with the feature: no band to offer, so nothing to offer.
            Assert.Empty(component.FindAll("[data-testid='roadmap-pane-option']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            Assert.Contains("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });

        _ = features.SetEnabled(RoadmapFeatures.Roadmap, true);

        component.WaitForAssertion(() =>
        {
            // Back to hidden, which is where the reader left it — not to the default.
            Assert.Equal("false", component.Find("[data-testid='roadmap-pane-option']").GetAttribute("aria-pressed"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            Assert.Contains("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });
    }

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    private static Harness CreateHarness(Action<AppFeatureSettingsStore>? configureFeatures = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-workspace-surface-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        // The three surfaces under test are on; everything that would put extra
        // chrome or a network call in the way is off.
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, true);
        _ = featureSettings.SetEnabled(MonitoringFeatures.Dashboard, true);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);
        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, false);
        _ = featureSettings.SetEnabled(AppFeatures.AiAssistant, false);
        _ = featureSettings.SetEnabled(AppFeatures.FeedbackReporting, false);
        _ = featureSettings.SetEnabled(BacklogFeatures.GitHubIntegration, false);

        configureFeatures?.Invoke(featureSettings);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories);
        var configuredRepository = repository with
        {
            CloneDirectory = RepositoryRoot.Root.FullName,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());
        var knowledgeFolderSource = new KnowledgeFolderSource(gitHubSettings, store);
        var repositoryBacklog = new RepositoryBacklogSource(
            BacklogTestHost.BacklogStoreFor(store, knowledgeFolderSource));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<ICopilotToolService, UnsupportedCopilotToolService>();
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(knowledgeFolderSource);
        context.Services.AddSingleton(repositoryBacklog);
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
        context.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddScoped(sp => BacklogTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            BacklogCopilotCli.Unavailable,
            sp.GetRequiredService<RepositoryBacklogSource>()));

        return new Harness(root, context);
    }

    private sealed record Harness(string Root, BunitContext Context) : IDisposable
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
