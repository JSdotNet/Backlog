namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The scenario-part checklist's words: the count, whether it proves anything, and
/// the quiet line under each part. A task and a delivery run both draw it, and the
/// task is the one that asks for a verdict, so the verdict is the host's to ask for.
/// </summary>
public sealed class ScenarioPartChecklistTests
{
    private const string Stem = "set-up-and-fill-the-backlog";

    private static ScenarioChecklistPart Part(string title, string state, string? lastRun = "7 Oct 2026 08:30", int? failedStep = null) =>
        new(title, state, Stem, lastRun, $".devbook/domain/work/{Stem}.md#{title.ToLowerInvariant().Replace(' ', '-')}", failedStep);

    [Fact]
    public void Every_part_passing_says_proved_in_the_count()
    {
        using var context = new BunitContext();

        var list = context.Render<ScenarioPartChecklist>(parameters => parameters
            .Add(p => p.Parts, [Part("Statuses are set up", "passed"), Part("An item is moved", "passed")])
            .Add(p => p.ShowProved, true));

        Assert.Equal("2 of 2 passing · proved", list.Find("[data-testid='scenario-checklist-count']").TextContent.Trim());
        Assert.NotNull(list.Find("[data-testid='scenario-checklist-proved']"));
    }

    [Fact]
    public void A_part_not_passing_says_not_proved_in_the_count()
    {
        using var context = new BunitContext();

        var list = context.Render<ScenarioPartChecklist>(parameters => parameters
            .Add(p => p.Parts, [Part("Statuses are set up", "passed"), Part("An item is moved", "stale")])
            .Add(p => p.ShowProved, true));

        Assert.Equal("1 of 2 passing · not proved", list.Find("[data-testid='scenario-checklist-count']").TextContent.Trim());
        Assert.Empty(list.FindAll("[data-testid='scenario-checklist-proved']"));
    }

    /// <summary>A delivery run's evidence is what one run did, not an entry's
    /// acceptance, so it gives no verdict.</summary>
    [Fact]
    public void Without_a_verdict_asked_for_the_count_is_only_the_count()
    {
        using var context = new BunitContext();

        var list = context.Render<ScenarioPartChecklist>(parameters => parameters
            .Add(p => p.Parts, [Part("Statuses are set up", "passed"), Part("An item is moved", "passed")]));

        Assert.Equal("2 of 2 passing", list.Find("[data-testid='scenario-checklist-count']").TextContent.Trim());
    }

    [Fact]
    public void The_quiet_line_names_the_page_stem_and_the_run()
    {
        using var context = new BunitContext();

        var list = context.Render<ScenarioPartChecklist>(parameters => parameters
            .Add(p => p.Parts, [Part("Statuses are set up", "passed"), Part("An item is restored", "never-run", lastRun: null)]));

        Assert.Equal(
            [$"{Stem} · 7 Oct 2026 08:30", $"{Stem} · Never run"],
            list.FindAll("[data-testid='scenario-checklist-part'] .scenario-checklist__run").Select(line => line.TextContent.Trim()));
    }

    [Fact]
    public void A_failed_part_adds_the_step_its_run_stopped_on()
    {
        using var context = new BunitContext();

        var list = context.Render<ScenarioPartChecklist>(parameters => parameters
            .Add(p => p.Parts,
            [
                Part("An item is moved", "failed", failedStep: 2),
                Part("An item is archived", "failed"),
                Part("An item is restored", "stale", failedStep: 3)
            ]));

        // A failure with no step recorded says nothing about one; a step on a part
        // that is not failed — stale, here — is not where anything broke now.
        Assert.Equal(
            [$"{Stem} · 7 Oct 2026 08:30 · step 2", $"{Stem} · 7 Oct 2026 08:30", $"{Stem} · 7 Oct 2026 08:30"],
            list.FindAll("[data-testid='scenario-checklist-part'] .scenario-checklist__run").Select(line => line.TextContent.Trim()));
    }

    [Fact]
    public void The_heading_names_the_list_to_a_screen_reader()
    {
        using var context = new BunitContext();

        var list = context.Render<ScenarioPartChecklist>(parameters => parameters
            .Add(p => p.Parts, [Part("Statuses are set up", "passed")])
            .Add(p => p.Heading, "Acceptance · scenario parts"));

        Assert.Equal("Acceptance · scenario parts", list.Find("section").GetAttribute("aria-label"));
        Assert.Equal("Acceptance · scenario parts", list.Find(".scenario-checklist__heading").TextContent.Trim());
    }
}
