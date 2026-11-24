using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.Devbook.Scenarios;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's page for a file it has no page of its own for — actors.md,
/// skills.md, a page a context adds such as import.md — reached as a reader
/// reaches it: the file's row picked. The file opens as written, in the view's
/// page style, under the runs of any journeys it holds; the placeholder is left for
/// no selection (domain/devbook requirement "Any other file opens as written").
/// </summary>
public sealed class DomainViewFilePageTests : IDisposable
{
    private readonly List<TemporaryDirectory> _temps = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Shaped as Fincent's financieel/actors.md: a typed title block naming the
    // file's related pages, a quoted lead with an annotation under it, then one
    // typed chapter per actor.
    private const string Actors = """
        # Who works with tasks

        ```meta
        type: actors
        status: review
        related: [.devbook/domain/tasks/domain.md, .devbook/domain/tasks/skills.md]
        ```

        > Who works with **tasks**: the people who plan and the people who do.

        ```annotation
        kind: question
        body: Is a reviewer an actor of its own?
        ```

        ## Planner

        ```meta
        type: user
        related: [.devbook/domain/tasks/domain.md#task]
        ```

        The planner orders the work for the week.

        ## Doer

        ```meta
        type: user
        ```

        The doer ticks a task off once it is finished.
        """;

    private const string Skills = """
        # Skills

        ```meta
        type: skills
        ```

        What an agent working on tasks can be asked to do.

        ## Triage

        An agent sorts the inbox into tasks.
        """;

    // Shaped as Fincent's financieel/import.md: a page of a kind the convention
    // does not name, its lead quoted, its chapters untyped.
    private const string Import = """
        # Import

        ```meta
        type: import
        related: [.devbook/domain/tasks/domain.md#task]
        ```

        > Everything tasks records about importing an old to-do list.

        ## What import is

        A one-off move of a list into tasks, without its history.

        ## Starting points

        - Done items are not imported.
        """;

    [Fact]
    public async Task Actors_opens_as_written_with_its_title_lead_chapters_and_related_links()
    {
        await using var harness = CreateHarness();
        var component = await OpenAsync(harness, "Actors");

        component.WaitForAssertion(() => Assert.Equal("Who works with tasks", component.Find("[data-testid='domain-file-title']").TextContent.Trim()), Patience);
        Assert.Equal("file", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));

        Assert.Equal("actors · review", component.Find("[data-testid='domain-file-kicker']").TextContent.Trim());
        var lead = component.Find("[data-testid='domain-file-lead']");
        Assert.Contains("the people who plan and the people who do", lead.TextContent);
        Assert.DoesNotContain("reviewer", lead.TextContent);

        Assert.Equal(
            ["Planner", "Doer"],
            component.FindAll("[data-testid='domain-file-chapter-title']").Select(title => title.TextContent.Trim()));
        Assert.Contains("orders the work for the week", component.Find("[data-testid='domain-file-chapter'][data-chapter='planner']").TextContent);
        Assert.Contains("ticks a task off", component.Find("[data-testid='domain-file-chapter'][data-chapter='doer']").TextContent);

        Assert.Equal(2, component.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']").Count);
        Assert.Single(component.FindAll("[data-testid='domain-file-chapter'][data-chapter='planner'] [data-testid='domain-file-chapter-related']"));
        Assert.Empty(component.FindAll("[data-testid='domain-file-chapter'][data-chapter='doer'] [data-testid='domain-file-chapter-related']"));
    }

    [Theory]
    [InlineData("Skills", "Skills", "Triage", "sorts the inbox")]
    [InlineData("Import", "Import", "What import is", "without its history")]
    public async Task Skills_and_a_page_the_convention_does_not_name_open_as_written(string row, string title, string chapter, string body)
    {
        await using var harness = CreateHarness();
        var component = await OpenAsync(harness, row);

        component.WaitForAssertion(() => Assert.Equal(title, component.Find("[data-testid='domain-file-title']").TextContent.Trim()), Patience);
        Assert.Equal("file", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));
        Assert.Single(component.FindAll("[data-testid='domain-file-lead']"));
        Assert.Contains(chapter, component.FindAll("[data-testid='domain-file-chapter-title']").Select(heading => heading.TextContent.Trim()));
        Assert.Contains(body, component.Find("[data-testid='domain-file-chapter']").TextContent);
    }

    [Fact]
    public async Task Following_a_related_link_opens_its_page()
    {
        await using var harness = CreateHarness();
        var component = await OpenAsync(harness, "Actors");

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']")), Patience);
        await component.InvokeAsync(() => component.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']")[0].Click());

        component.WaitForAssertion(() => Assert.Equal("tasks/domain.md", component.FindComponent<DomainView>().Instance.SelectedPath), Patience);
        Assert.Equal("domain", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
    }

    [Fact]
    public async Task No_selection_still_shows_the_placeholder()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, (string?)null));

        Assert.Single(view.FindAll("[data-testid='domain-view-placeholder']"));
        Assert.Equal("none", view.Find("[data-testid='domain-view']").GetAttribute("data-page"));
    }

    [Fact]
    public async Task A_path_that_names_no_file_says_so()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/missing.md"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-view-file-problem']")), Patience);
        Assert.Equal("file", view.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(view.FindAll("[data-testid='domain-view-placeholder']"));
    }

    [Theory]
    [InlineData("../secret.md")]
    [InlineData("tasks/../../secret.md")]
    [InlineData("{root}/secret.md")]
    public async Task A_path_out_of_the_folder_says_so_and_reads_nothing(string selected)
    {
        await using var harness = CreateHarness();
        var secret = SecretPath(harness);

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, selected.Replace("{root}", Path.GetDirectoryName(secret)!.Replace('\\', '/'), StringComparison.Ordinal)));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-view-file-problem']")), Patience);
        Assert.Empty(view.FindAll("[data-testid='domain-file-title']"));
        Assert.DoesNotContain(SecretText, view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_store_reads_no_file_outside_the_domain_folder()
    {
        await using var harness = CreateHarness();
        var secret = SecretPath(harness);
        var domain = await harness.Context.Services.GetRequiredService<DomainDevbookStore>().LoadAsync(harness.RepositoryAlias, TestContext.Current.CancellationToken);

        Assert.NotNull(DomainDevbookStore.ReadText(domain, ".domain/tasks/actors.md"));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/../secret.md"));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/tasks/../../secret.md"));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/" + secret.Replace('\\', '/')));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/" + secret));
    }

    [Fact]
    public void Within_root_keeps_a_path_inside_and_refuses_one_that_leaves()
    {
        var root = Path.Combine(Path.GetTempPath(), "within-root", ".domain");
        var outside = Path.Combine(Path.GetTempPath(), "within-root", "secret.md");

        Assert.Equal(Path.Combine(root, "tasks", "actors.md"), DomainDevbookStore.WithinRoot(root, "tasks/actors.md"));
        Assert.Null(DomainDevbookStore.WithinRoot(root, "../secret.md"));
        Assert.Null(DomainDevbookStore.WithinRoot(root, outside));
        Assert.Null(DomainDevbookStore.WithinRoot(root, outside.Replace('\\', '/')));
        // A sibling folder that only shares the root's name as a prefix is outside too.
        Assert.Null(DomainDevbookStore.WithinRoot(root, "../.domain-other/x.md"));
    }

    /// <summary>
    /// A scenarios file opens with its journey's run above it: the state, the E2E
    /// summary, the setup it runs under, and each part with its outcome and its
    /// screenshot — the screenshot drawn from the run's PNG, with the line that says
    /// which journey, part, state and time it came from. The file itself follows,
    /// as written.
    /// </summary>
    [Fact]
    public async Task A_scenarios_file_shows_its_journeys_setup_parts_outcomes_and_screenshots()
    {
        await using var harness = CreateHarness();
        WriteScenarioContext(harness.Root, failSecondPart: true);

        var component = RenderScenarios(harness);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-scenario-page']")), Patience);
        Assert.Equal("file", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Equal("failed", component.Find("[data-testid='devbook-scenario-page']").GetAttribute("data-state"));
        var setup = component.Find("[data-testid='devbook-scenario-setup']").TextContent;
        Assert.Contains("/orders/new", setup, StringComparison.Ordinal);
        Assert.Contains("actors.md#shopper", setup, StringComparison.Ordinal);
        Assert.Contains("default", setup, StringComparison.Ordinal);
        Assert.Contains("1 of 2 failed", component.Find("[data-testid='devbook-scenario-summary']").TextContent, StringComparison.Ordinal);

        var parts = component.FindAll("[data-testid='devbook-scenario-part']");
        Assert.Equal(["the-cart-is-filled", "the-order-is-placed"], parts.Select(part => part.GetAttribute("data-anchor")));
        Assert.Equal(["Passed", "Failed"], component.FindAll("[data-testid='devbook-scenario-part-outcome']").Select(badge => badge.TextContent.Trim()));
        Assert.Equal("Scenarios", component.Find("[data-testid='domain-file-title']").TextContent.Trim());

        await EventuallyAsync(component, () => Assert.Contains(
            "src=\"data:image/png;base64,",
            Between(component.Markup, "data-testid=\"devbook-scenario-parts\"", "</ol>"),
            StringComparison.Ordinal));
        Assert.Contains("Place an order › The cart is filled · failed", component.Find("[data-testid='devbook-scenario-part'] [data-testid='devbook-scenario-shot-source']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A journey whose text moved on since its run is stale, and says which
    /// signature the run proved.</summary>
    [Fact]
    public async Task A_journey_edited_since_its_run_is_stale()
    {
        await using var harness = CreateHarness();
        WriteScenarioContext(harness.Root, failSecondPart: false);
        var page = Path.Combine(harness.Root, ".domain", "orders", "scenarios.md");
        File.WriteAllText(page, File.ReadAllText(page).Replace("**Then** the cart holds one lamp", "**Then** the cart holds two lamps", StringComparison.Ordinal));

        var component = RenderScenarios(harness);

        component.WaitForAssertion(() => Assert.Equal("stale", component.Find("[data-testid='devbook-scenario-page']").GetAttribute("data-state")), Patience);
        Assert.Single(component.FindAll("[data-testid='devbook-scenario-stale-note']"));
    }

    [Fact]
    public async Task A_journey_with_no_run_reads_never_run()
    {
        await using var harness = CreateHarness();
        WriteScenarioContext(harness.Root, failSecondPart: false, withRun: false);

        var component = RenderScenarios(harness);

        component.WaitForAssertion(() => Assert.Equal("never-run", component.Find("[data-testid='devbook-scenario-page']").GetAttribute("data-state")), Patience);
        Assert.Contains("never run", component.Find("[data-testid='devbook-scenario-summary']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A scenarios file holding two journeys shows each one's run above it,
    /// in file order.</summary>
    [Fact]
    public async Task A_scenarios_file_shows_a_run_for_each_of_its_journeys()
    {
        await using var harness = CreateHarness();
        WriteScenarioContext(harness.Root, failSecondPart: true);
        var file = Path.Combine(harness.Root, ".domain", "orders", "scenarios.md");
        File.AppendAllText(file, "\n## Cancel an order\n\n```meta\ntype: scenario\n```\n\n### The order is cancelled\n- **When** I cancel it\n");

        var component = RenderScenarios(harness);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='devbook-scenario-page']").Count), Patience);
        Assert.Equal(["failed", "never-run"], component.FindAll("[data-testid='devbook-scenario-page']").Select(page => page.GetAttribute("data-state")));
        Assert.Equal(3, component.FindAll("[data-testid='devbook-scenario-part']").Count);
    }

    /// <summary>Any other file holds no journey, so no run is drawn above it.</summary>
    [Fact]
    public async Task A_file_with_no_journey_draws_no_run()
    {
        await using var harness = CreateHarness();
        WriteScenarioContext(harness.Root, failSecondPart: false);

        var component = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/actors.md"));

        component.WaitForAssertion(() => Assert.Equal("Who works with tasks", component.Find("[data-testid='domain-file-title']").TextContent.Trim()), Patience);
        Assert.Empty(component.FindAll("[data-testid='devbook-scenario-page']"));
    }

    /// <summary>
    /// A scenarios file opened at a part — pressed in an entry's Acceptance checklist —
    /// marks that part as the current one and asks the library's reveal to scroll it
    /// into view, once; the other parts stay as they were.
    /// </summary>
    [Fact]
    public async Task A_scenarios_file_opened_at_a_part_marks_it_and_scrolls_to_it()
    {
        await using var harness = CreateHarness();
        WriteScenarioContext(harness.Root, failSecondPart: false);

        var component = RenderScenarios(harness, new DevbookFocus("the-order-is-placed", 1));

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='devbook-scenario-part']").Count), Patience);
        var focused = Assert.Single(component.FindAll("[data-testid='devbook-scenario-part'][aria-current='true']"));
        Assert.Equal("the-order-is-placed", focused.GetAttribute("data-anchor"));
        Assert.Contains("devbook-scenario-part--focused", focused.ClassList);
        Assert.DoesNotContain("devbook-scenario-part--focused", component.Find("[data-testid='devbook-scenario-part'][data-anchor='the-cart-is-filled']").ClassList);

        component.WaitForAssertion(() => Assert.Single(harness.Context.JSInterop.Invocations["backlogRevealRow"]), Patience);
        Assert.Equal("[data-testid='devbook-scenario-part'][aria-current='true']", harness.Context.JSInterop.Invocations["backlogRevealRow"][0].Arguments[0]);

        // A screenshot landing re-renders the page; the reader is not pulled back.
        await EventuallyAsync(component, () => Assert.Contains("src=\"data:image/png;base64,", component.Markup, StringComparison.Ordinal));
        Assert.Single(harness.Context.JSInterop.Invocations["backlogRevealRow"]);
    }

    /// <summary>An anchor no part has, or none, marks nothing and scrolls nowhere —
    /// and the journey draws as it always did.</summary>
    [Theory]
    [InlineData("no-such-part")]
    [InlineData(null)]
    public async Task A_scenarios_file_opened_at_no_part_marks_nothing(string? anchor)
    {
        await using var harness = CreateHarness();
        WriteScenarioContext(harness.Root, failSecondPart: false);

        var component = RenderScenarios(harness, anchor is null ? null : new DevbookFocus(anchor, 1));

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='devbook-scenario-part']").Count), Patience);
        Assert.Empty(component.FindAll("[aria-current='true']"));
        Assert.Empty(component.FindAll(".devbook-scenario-part--focused"));
        Assert.Empty(harness.Context.JSInterop.Invocations["backlogRevealRow"]);
    }

    /// <summary>The <c>orders</c> scenarios file opened in the Domain view, at the
    /// part a followed reference names when it names one.</summary>
    private static IRenderedComponent<DomainView> RenderScenarios(Harness harness, DevbookFocus? focus = null) =>
        harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "orders/scenarios.md")
            .Add(v => v.Focus, focus));

    /// <summary>A screenshot lands from its own component's read after the render
    /// that drew the page, and under a parallel suite that render does not always
    /// reach the view's waiter or its parsed nodes — so this asks the markup again
    /// until it holds rather than waiting on a render.</summary>
    private static async Task EventuallyAsync(IRenderedComponent<DomainView> component, Action assertion)
    {
        var until = DateTime.UtcNow + Patience;
        while (true)
        {
            try
            {
                assertion();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < until)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                // Lets the renderer run what the read queued for it.
                await component.InvokeAsync(() => { });
            }
        }
    }

    private static string Between(string markup, string from, string to)
    {
        var start = markup.IndexOf(from, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = markup.IndexOf(to, start, StringComparison.Ordinal);
        return end < 0 ? markup[start..] : markup[start..end];
    }

    /// <summary>An <c>orders</c> context with one journey in its scenarios file and —
    /// unless asked not to — a version 3 run of it whose signature is the journey's
    /// own.</summary>
    private static void WriteScenarioContext(string root, bool failSecondPart, bool withRun = true)
    {
        var folder = Path.Combine(root, ".domain", "orders");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "context.md"), "# Orders\n\n```meta\nstatus: draft\nindex: root\ntype: context\n```\n\nOrders prose.\n");
        const string page = "# Scenarios\n\n```meta\ntype: scenarios\n```\n\n## Place an order\n\n```meta\ntype: scenario\nstart: /orders/new\nactor: actors.md#shopper\n```\n\nA shopper fills a cart and places the order.\n\n### The cart is filled\n- **Given** an empty cart\n- **Then** the cart holds one lamp\n\n![The filled cart](shot:cart-filled)\n\n### The order is placed\n- **When** I place the order\n\n![The confirmation](shot:order-placed)\n";
        File.WriteAllText(Path.Combine(folder, "scenarios.md"), page);
        if (!withRun) return;

        var signature = ScenarioSignature.Of(Assert.Single(ScenarioPageParser.ParseFile(page, ".domain/orders/scenarios.md").Scenarios));
        var run = Path.Combine(root, ".domain", "orders", "scenarios", "place-an-order");
        Directory.CreateDirectory(run);
        File.WriteAllBytes(Path.Combine(run, "cart-filled.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var outcome = failSecondPart ? "failed" : "passed";
        File.WriteAllText(Path.Combine(run, "run.json"), $$"""
            {
              "version": 3,
              "journey": ".domain/orders/scenarios.md#place-an-order",
              "signature": "{{signature}}",
              "ranAt": "2026-10-07T08:30:00Z",
              "profile": "default",
              "parts": [
                { "title": "The cart is filled", "anchor": "the-cart-is-filled", "outcome": "passed", "durationMs": 1200 },
                { "title": "The order is placed", "anchor": "the-order-is-placed", "outcome": "{{outcome}}", "durationMs": 800 }
              ],
              "shots": {
                "cart-filled": { "file": "cart-filled.png", "part": "the-cart-is-filled", "ranAt": "2026-10-07T08:30:00Z" }
              }
            }
            """);
    }

    private const string SecretText = "Nobody outside the folder reads this.";

    /// <summary>A file beside the Domain folder, outside it, that no Domain page
    /// may read.</summary>
    private static string SecretPath(Harness harness)
    {
        var secret = Path.Combine(harness.Root, "secret.md");
        File.WriteAllText(secret, $"# Secret\n\n{SecretText}\n");
        return secret;
    }

    /// <summary>Renders the pane, opens the Tasks context and picks one of its rows.</summary>
    private static async Task<IRenderedComponent<DevbookPane>> OpenAsync(Harness harness, string row)
    {
        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, row)), Patience);
        await ClickMenuItemAsync(component, row);
        return component;
    }

    /// <summary>One menu row, by the label a reader reads; null while it is not drawn.</summary>
    private static IElement? MenuItem(IRenderedComponent<DevbookPane> component, string label) =>
        component.FindAll(".devbook-menu__item")
            .SingleOrDefault(item => string.Equals(item.QuerySelector(".devbook-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    /// <summary>Presses one menu row, found and clicked in one dispatch so a load
    /// landing in between cannot take the row's handler away.</summary>
    private static Task ClickMenuItemAsync(IRenderedComponent<DevbookPane> component, string label) =>
        component.InvokeAsync(() => MenuItem(component, label)!.Click());

    private Harness CreateHarness()
    {
        var temp = new TemporaryDirectory("backlog-domain-view-file-page");
        _temps.Add(temp);
        var root = temp.Path;
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), "# Domain: Tasks\n\n```meta\nstatus: draft\n```\n\nWhat the tasks context is.\n\n## Task\n\n```meta\ntype: aggregate\n```\n\nA piece of work.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "actors.md"), Actors);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "skills.md"), Skills);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "import.md"), Import);
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        // The pages draw the status their chapters state only while the Devbook
        // status switch is on; these read it.
        Assert.Null(features.SetEnabled(DevbookFeatures.Status, true));

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
        context.Services.AddSingleton(sp => new DevbookScenarioStore(sp.GetRequiredService<IDevbookFolderSource>()));

        return new Harness(context, repository.Alias, root);
    }

    public void Dispose()
    {
        foreach (var temp in _temps)
        {
            temp.Dispose();
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias, string Root) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
