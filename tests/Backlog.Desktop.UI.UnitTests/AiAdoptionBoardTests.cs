using Backlog.Desktop.UI.Devbook;
using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;
using Backlog.UI.Components.Devbook;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The AI model read into the adoption board, as <c>devbook-ai.md</c> ("The loop
/// picture", Adoption) lays it out, and the tab opening a card's chapter.
/// </summary>
public sealed class AiAdoptionBoardTests
{
    [Fact]
    public void The_rows_are_the_seven_stages_numbered_then_the_three_layers()
    {
        var board = AiAdoptionBoard.From(AiModel.From([]));

        Assert.Equal(
            ["decide", "specify", "code", "verify", "integrate", "release", "operate", "thread", "upkeep", "harness"],
            board.Homes.Select(home => home.Key));
        Assert.Equal(["01", "02", "03", "04", "05", "06", "07", null, null, null], board.Homes.Select(home => home.Number));
        Assert.Equal(
            [.. Enumerable.Repeat(AdoptionHomeGroup.BeforePullRequest, 4), .. Enumerable.Repeat(AdoptionHomeGroup.AfterPullRequest, 3), .. Enumerable.Repeat(AdoptionHomeGroup.Underneath, 3)],
            board.Homes.Select(home => home.Group));
        Assert.Empty(board.Cards);
    }

    [Fact]
    public void A_card_sits_at_its_first_stage_else_its_first_layer_and_names_the_rest()
    {
        var model = AiModel.From([
            File("02-code.md",
                Section("Code gate", status: "adopted", stage: ["code", "verify"], layer: ["harness"], evidence: "recorded", date: "2026-09-26",
                    body: [Bullets(("Evidence", " — every merged pull request carries a run."))]),
                Section("Worktree per change", status: "trial", layer: ["harness", "upkeep"], evidence: "fired",
                    body: [Bullets(("Moves up when", " — a schedule publishes."))]),
                Section("Off the ladder", status: "experimental", stage: ["code"]),
                Section("Nowhere", status: "trial"))
        ]);

        var board = AiAdoptionBoard.From(model);

        Assert.Equal(2, board.Cards.Count);

        var gate = board.Cards[0];
        Assert.Equal("code", gate.HomeKey);
        Assert.Equal(AdoptionRung.Adopted, gate.Rung);
        Assert.Equal(AdoptionEvidence.Recorded, gate.Evidence);
        Assert.Equal("2026-09-26", gate.Since);
        Assert.Equal("every merged pull request carries a run.", gate.EvidenceText);
        Assert.Null(gate.MovesUpWhen);
        Assert.Equal(["Verify", "Agent harness"], gate.Reach);

        var worktree = board.Cards[1];
        Assert.Equal("harness", worktree.HomeKey);
        Assert.Equal(AdoptionRung.Trial, worktree.Rung);
        Assert.Equal(AdoptionEvidence.Fired, worktree.Evidence);
        Assert.Equal("a schedule publishes.", worktree.MovesUpWhen);
        Assert.Equal(["Upkeep loop"], worktree.Reach);
        Assert.False(gate.Scheduled);

        Assert.Same(model.Usages[0], AiAdoptionBoard.UsageOf(model, gate));
        Assert.Same(model.Usages[1], AiAdoptionBoard.UsageOf(model, worktree));
    }

    [Fact]
    public void A_usage_that_depends_on_the_delivery_schedule_is_a_scheduled_card()
    {
        var scheduled = new DocumentDevbookSection("Issue sweep", "ai-issue-sweep", new MetadataRecord
        {
            Status = "trial",
            Extra = new Dictionary<string, IReadOnlyList<string>> { ["stage"] = ["decide"] },
            DependsOn = [DevbookReference.Parse(".devbook/tech/ai-development.md#delivery-schedule")!]
        }, []);
        var model = AiModel.From([File("01-decide.md", scheduled, Section("Tracker", status: "adopted", stage: ["decide"]))]);

        var board = AiAdoptionBoard.From(model);

        Assert.Equal([true, false], board.Cards.Select(card => card.Scheduled));
    }

    [Fact]
    public void Picking_a_card_opens_its_chapter()
    {
        var model = AiModel.From([
            File("03-test.md", Section("QA agent", status: "adopted", stage: ["verify"], evidence: "recorded"))
        ]);
        DevbookChapterLink? opened = null;

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = context.Render<AiAdoptionView>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.OnNavigateToChapter, (DevbookChapterLink link) => opened = link));

        view.Find("[data-testid='adoption-card']").Click();

        Assert.Equal(model.Usages[0].Link, opened);
    }

    private static DocumentDevbookList Bullets(params (string Lead, string Text)[] items) =>
        new(false, [.. items.Select(item => (IReadOnlyList<MdInline>)[new MdStrong(item.Lead), new MdText(item.Text)])]);

    private static DocumentDevbookFile File(string name, params DocumentDevbookSection[] sections) =>
        new(name, name, string.Empty, MetadataRecord.Empty, sections);

    private static DocumentDevbookSection Section(
        string heading,
        string? status = null,
        string[]? stage = null,
        string[]? layer = null,
        string? evidence = null,
        string? date = null,
        DocumentDevbookBlock[]? body = null)
    {
        var extra = new Dictionary<string, IReadOnlyList<string>>();
        if (stage is not null) extra["stage"] = stage;
        if (layer is not null) extra["layer"] = layer;
        if (evidence is not null) extra["evidence"] = [evidence];

        var meta = new MetadataRecord { Status = status, Date = date, Extra = extra };
        return new DocumentDevbookSection(heading, $"ai-{heading}", meta, body ?? []);
    }
}
