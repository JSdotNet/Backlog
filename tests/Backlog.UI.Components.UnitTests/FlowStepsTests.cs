using Backlog.UI.Components.Feedback;

namespace Backlog.UI.Components.UnitTests;

public sealed class FlowStepsTests
{
    private static readonly FlowStep[] Steps =
    [
        new("Scope Discovery", FlowStepTone.Done, "Done", "1m 0s", [new("Explore", "claude-opus-5-5")]),
        new("Implementation", FlowStepTone.Active, "In progress"),
        new("Build & Test", FlowStepTone.Blocked, "Blocked"),
        new("Verification", FlowStepTone.Skipped, "Skipped"),
        new("Summary", FlowStepTone.Pending, "Pending")
    ];

    [Fact]
    public void The_steps_are_an_ordered_list_in_the_order_given()
    {
        using var context = new BunitContext();

        var flow = context.Render<FlowSteps>(parameters => parameters
            .Add(p => p.Steps, Steps)
            .Add(p => p.AriaLabel, "Stages")
            .Add(p => p.TestId, "stages"));

        var ol = flow.Find("ol.flow-steps");
        Assert.Equal("Stages", ol.GetAttribute("aria-label"));
        Assert.Equal("stages", ol.GetAttribute("data-testid"));

        Assert.Equal(
            ["Scope Discovery", "Implementation", "Build & Test", "Verification", "Summary"],
            flow.FindAll(".flow-step__title").Select(title => title.TextContent.Trim()));
    }

    /// <summary>The tone is drawn — a border and a mark — and said: the status word
    /// is text on every step, so nothing depends on seeing the colour.</summary>
    [Fact]
    public void Each_step_wears_its_tone_and_says_its_status_in_words()
    {
        using var context = new BunitContext();

        var flow = context.Render<FlowSteps>(parameters => parameters.Add(p => p.Steps, Steps));

        var items = flow.FindAll("[data-testid='flow-step']");

        Assert.Equal(
            ["done", "active", "blocked", "skipped", "pending"],
            items.Select(item => item.GetAttribute("data-tone")));
        Assert.Contains("flow-step--done", items[0].ClassName, StringComparison.Ordinal);
        Assert.Contains("flow-step--active", items[1].ClassName, StringComparison.Ordinal);

        Assert.Equal("In progress", items[1].QuerySelector(".flow-step__status")!.TextContent.Trim());
        Assert.Equal("true", items[0].QuerySelector(".flow-step__mark")!.GetAttribute("aria-hidden"));

        // The step the flow is on is announced as the current one.
        Assert.Equal("step", items[1].GetAttribute("aria-current"));
        Assert.Null(items[0].GetAttribute("aria-current"));
    }

    [Fact]
    public void Facts_follow_the_status_and_notes_list_under_it()
    {
        using var context = new BunitContext();

        var flow = context.Render<FlowSteps>(parameters => parameters.Add(p => p.Steps, Steps));

        var first = flow.FindAll("[data-testid='flow-step']")[0];
        Assert.Equal("Done · 1m 0s", first.QuerySelector(".flow-step__status")!.TextContent.Trim());

        var note = Assert.Single(first.QuerySelectorAll(".flow-step__note"));
        Assert.Equal("Explore", note.QuerySelector(".flow-step__note-label")!.TextContent.Trim());
        Assert.Equal("claude-opus-5-5", note.QuerySelector(".flow-step__note-detail")!.TextContent.Trim());

        // A step with no notes draws no empty list.
        Assert.Null(flow.FindAll("[data-testid='flow-step']")[1].QuerySelector(".flow-step__notes"));
    }

    [Fact]
    public void A_note_without_detail_draws_its_label_alone()
    {
        using var context = new BunitContext();

        var flow = context.Render<FlowSteps>(parameters => parameters
            .Add(p => p.Steps, [new FlowStep("Validation", FlowStepTone.Done, "Done", Notes: [new("qa:qa")])]));

        var note = flow.Find(".flow-step__note");
        Assert.Equal("qa:qa", note.TextContent.Trim());
        Assert.Null(note.QuerySelector(".flow-step__note-detail"));
    }

    [Fact]
    public void The_host_class_is_added_to_the_library_one()
    {
        using var context = new BunitContext();

        var flow = context.Render<FlowSteps>(parameters => parameters
            .Add(p => p.Steps, Steps)
            .Add(p => p.CssClass, "sessions-run__flow"));

        Assert.Equal("flow-steps sessions-run__flow", flow.Find("ol").ClassName);
    }
}
