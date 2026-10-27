using Backlog.Desktop.UI.Devbook;

using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using static Backlog.Desktop.UI.UnitTests.DevbookStackStampsTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The AI section's Devbook tab draws the stack's stamps: one card per
/// component, the devbook card's folders, procedures, files and migrations, the
/// schedule card's enabled schedules, and where each component works on the
/// path. <see cref="DevbookStackStampsTests"/> owns how the stamps are read.
/// </summary>
public sealed class DevbookAdoptionViewTests : IAsyncDisposable
{
    private readonly BunitContext _context = new();

    private static readonly DevbookStackStamps FullStack = new(
        new DevbookComponentStamp("1.28.0"),
        new DevbookPluginStamp(
            "1.30.1",
            "36",
            ["arc42", "ai", "openspec"],
            ["run", "capture"],
            ManagedFiles: 3,
            UnmanagedFiles: 1,
            [new DevbookMigrationStamp("033-c4-workspace", "2026-10-20"), new DevbookMigrationStamp("035-delivery-path-stages", null)]),
        new DevbookScheduleStamp("1.26.0", ["issue-sweep", "merge-review"]));

    private static readonly AiModel Model = AiModel.From([
        File("05-unattended-runs.md",
            Section("Issue sweep", stage: ["code", "verify"], layer: ["upkeep"], dependsOn: [".devbook/tech/ai-development.md#delivery-schedule"]))
    ]);

    [Fact]
    public void A_full_stack_draws_one_card_per_component_with_its_stamped_version()
    {
        var view = Render(FullStack);

        Assert.Equal("1.28.0", view.Find("[data-testid='devbook-adoption-delivery-version']").TextContent.Trim());
        Assert.Equal("1.30.1", view.Find("[data-testid='devbook-adoption-devbook-version']").TextContent.Trim());
        Assert.Equal("1.26.0", view.Find("[data-testid='devbook-adoption-schedule-version']").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-testid$='-not-adopted']"));
        Assert.Empty(view.FindAll("[data-testid='devbook-adoption-empty']"));
    }

    [Fact]
    public void The_devbook_card_draws_its_contract_folders_procedures_files_and_migrations()
    {
        var view = Render(FullStack);

        Assert.Equal("36", view.Find("[data-testid='devbook-adoption-devbook-contract']").TextContent.Trim());
        Assert.Equal("4 — 3 managed, 1 unmanaged", view.Find("[data-testid='devbook-adoption-devbook-files']").TextContent.Trim());

        // A folder the pane has a section for is a control; one it has not stays text.
        var folders = view.Find("[data-testid='devbook-adoption-folders']");
        Assert.Equal(["arc42", "ai"], folders.QuerySelectorAll("button").Select(button => button.TextContent.Trim()));
        Assert.Contains("openspec", folders.TextContent, StringComparison.Ordinal);

        Assert.Equal(["run", "capture"], view.Find("[data-testid='devbook-adoption-procedures']").Children.Select(chip => chip.TextContent.Trim()));

        var migrations = view.FindAll("[data-testid='devbook-adoption-migrations'] li");
        Assert.Equal(2, migrations.Count);
        Assert.Contains("033-c4-workspace", migrations[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("2026-10-20", migrations[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_adopted_folder_opens_its_section()
    {
        DevbookChapterLink? opened = null;
        var view = Render(FullStack, link => opened = link);

        view.Find("[data-testid='devbook-adoption-folders'] button").Click();

        Assert.NotNull(opened);
        Assert.Equal("arc42", opened.AreaKey);
        Assert.True(opened.IsFolder);
    }

    [Fact]
    public void The_schedule_card_lists_the_enabled_schedules()
    {
        var view = Render(FullStack);

        Assert.Equal(["issue-sweep", "merge-review"], view.Find("[data-testid='devbook-adoption-schedules']").Children.Select(chip => chip.TextContent.Trim()));
    }

    [Fact]
    public void Each_component_works_at_the_stages_and_layers_of_the_chapters_that_depend_on_it()
    {
        DevbookChapterLink? opened = null;
        var view = Render(FullStack, link => opened = link);

        var schedule = view.Find("[data-testid='devbook-adoption-schedule-usages']");
        Assert.Equal("Issue sweep", schedule.QuerySelector("button")!.TextContent.Trim());
        Assert.Equal("Stages: Code, Verify", schedule.QuerySelector("[data-testid='devbook-adoption-usage-stages']")!.TextContent.Trim());
        Assert.Equal("Layers: Upkeep loop", schedule.QuerySelector("[data-testid='devbook-adoption-usage-layers']")!.TextContent.Trim());

        Assert.Contains("No AI usage chapter depends on it.", view.Find("[data-testid='devbook-adoption-delivery-usages']").TextContent, StringComparison.Ordinal);

        view.Find("[data-testid='devbook-adoption-schedule-usages'] button").Click();
        Assert.Equal("ai", opened?.AreaKey);
        Assert.Equal("issue-sweep", opened?.Anchor);
    }

    [Fact]
    public void A_missing_component_is_drawn_as_not_adopted()
    {
        var view = Render(FullStack with { Delivery = null, Schedule = null });

        Assert.Single(view.FindAll("[data-testid='devbook-adoption-delivery-not-adopted']"));
        Assert.Single(view.FindAll("[data-testid='devbook-adoption-schedule-not-adopted']"));
        Assert.Single(view.FindAll("[data-testid='devbook-adoption-devbook-version']"));
        Assert.Empty(view.FindAll("[data-testid='devbook-adoption-schedules']"));
        Assert.Empty(view.FindAll(".alert, [role='alert']"));
    }

    [Fact]
    public void No_configuration_draws_the_empty_state()
    {
        var view = Render(null);

        Assert.Single(view.FindAll("[data-testid='devbook-adoption-empty']"));
        Assert.Empty(view.FindAll(".card"));
    }

    /// <summary>The section reads the configuration beside the folder it draws:
    /// the repository's own <c>.devbook/config.json</c>, and with none the tab
    /// shows the empty state.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_ai_section_hands_the_tab_the_repository_s_configuration(bool withConfiguration)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-adoption-tests", Guid.NewGuid().ToString("n"));
        var repository = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, ".ai"));
        Directory.CreateDirectory(Path.Combine(repository, ".devbook"));
        System.IO.File.WriteAllText(Path.Combine(repository, ".ai", "adoption-map.md"), """
            # AI adoption map
            ```meta
            status: adopted
            type: adoption-map
            ```

            How this project develops with AI.

            ## The loop

            ```mermaid
            flowchart LR
                plan --> code
            ```
            """);
        System.IO.File.WriteAllText(Path.Combine(repository, ".ai", "01-specify.md"), """
            # Specify
            ```meta
            status: adopted
            type: stage
            ```

            Turning an intent into a chapter.

            ## Devbook skills
            ```meta
            status: trial
            type: skill
            stage: [specify]
            ```

            - **Used for** — drafting chapters.
            """);
        if (withConfiguration)
        {
            System.IO.File.WriteAllText(
                Path.Combine(repository, ".devbook", "config.json"),
                """{ "components": { "delivery": { "pluginVersion": "9.8.7" } } }""");
        }

        try
        {
            var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
            var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
            Assert.Empty(errors);
            Assert.Null(gitHubSettings.SetRepositories(repositories));
            gitHubSettings.SetCloneDirectory("backlog", repository);

            await using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings));
            context.Services.AddSingleton<AiDevbookProvider>();
            context.Services.AddSingleton<DevbookChapterWriter>();

            var component = context.Render<AiDevbookView>(parameters => parameters.Add(view => view.RepositoryAlias, "backlog"));
            component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='ai-devbook-tab']")));
            component.Find("[data-testid='ai-devbook-tab']").Click();

            component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-adoption-view']")));
            if (withConfiguration)
            {
                Assert.Equal("9.8.7", component.Find("[data-testid='devbook-adoption-delivery-version']").TextContent.Trim());
                Assert.Single(component.FindAll("[data-testid='devbook-adoption-devbook-not-adopted']"));
            }
            else
            {
                Assert.Single(component.FindAll("[data-testid='devbook-adoption-empty']"));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private IRenderedComponent<DevbookAdoptionView> Render(DevbookStackStamps? stamps, Action<DevbookChapterLink>? onNavigate = null) =>
        _context.Render<DevbookAdoptionView>(parameters => parameters
            .Add(view => view.Model, Model)
            .Add(view => view.Stamps, stamps)
            .Add(view => view.OnNavigateToChapter, link => onNavigate?.Invoke(link)));

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();
}
