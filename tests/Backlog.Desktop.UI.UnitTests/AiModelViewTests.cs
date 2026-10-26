using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Metadata;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Model tab draws <see cref="AiModel"/> on the library's delivery path board:
/// these tests pin how the model's stages become cards — sub-stages from
/// <c>substage</c>, one unnamed card for the rest, "no AI" for an empty stage —
/// and that opening a usage hands its own chapter to the host.
/// </summary>
public sealed class AiModelViewTests
{
    private static readonly AiModel Model = AiModel.From([
        File("02-code.md",
            Section("Code gate", "adopted", stage: ["code", "verify"], substage: ["code/start", "code/implement", "verify/build-test"], layer: ["thread"]),
            Section("Plan items pasted in", "adopted", stage: ["code"], substage: ["code/start"]),
            Section("Worktree per change", "adopted", layer: ["harness"])),
        File("03-test.md",
            Section("QA agent", "trial", stage: ["verify"]))
    ]);

    [Fact]
    public void A_stage_s_sub_stages_are_its_cards_and_its_other_usages_share_one_unnamed_card()
    {
        var code = AiModelView.Stage(Stage("code"));
        Assert.Equal(["Start", "Implement"], code.Cards.Select(card => card.Name));
        Assert.Equal(["Code gate", "Plan items pasted in"], code.Cards[0].Usages.Select(usage => usage.Name));
        Assert.Equal("your session", code.Where);

        // The QA agent names no sub-stage of Verify, so it sits in an unnamed card
        // after the named one rather than being dropped.
        var verify = AiModelView.Stage(Stage("verify"));
        Assert.Equal(["Build test", null], verify.Cards.Select(card => card.Name));
        Assert.Equal(["QA agent"], verify.Cards[1].Usages.Select(usage => usage.Name));
    }

    [Fact]
    public void An_empty_stage_is_one_empty_unnamed_card()
    {
        var release = AiModelView.Stage(Stage("release"));

        var card = Assert.Single(release.Cards);
        Assert.Null(card.Name);
        Assert.True(card.IsEmpty);
        Assert.Equal("on main", release.Where);
    }

    [Fact]
    public void The_board_draws_seven_stages_three_layers_and_lights_a_usage_everywhere_it_sits()
    {
        using var context = new BunitContext();
        var view = context.Render<AiModelView>(parameters => parameters.Add(p => p.Model, Model));

        Assert.Equal(7, view.FindAll(".delivery-path__head").Count);
        Assert.Equal(3, view.FindAll(".delivery-path__layer").Count);
        Assert.Contains("Integrate", view.Find("[data-testid='ai-model-board-pull-request']").ParentElement!.TextContent);

        view.FindAll("[data-testid='ai-model-board-usage']").First(chip => chip.TextContent.Contains("Code gate")).Click();

        // Code › Start, Code › Implement, Verify › Build test and the thread row.
        Assert.Equal(4, view.FindAll(".delivery-path__card--lit").Count);
    }

    [Fact]
    public void Opening_a_usage_hands_the_host_its_own_chapter()
    {
        using var context = new BunitContext();
        var opened = new List<DevbookChapterLink>();
        var view = context.Render<AiModelView>(parameters => parameters
            .Add(p => p.Model, Model)
            .Add(p => p.OnNavigateToChapter, (DevbookChapterLink link) => opened.Add(link)));

        view.FindAll("[data-testid='ai-model-board-usage']").First(chip => chip.TextContent.Contains("QA agent")).Click();
        view.Find("[data-testid='ai-model-board-open']").Click();

        var link = Assert.Single(opened);
        Assert.Equal(Model.Usages.Single(usage => usage.Heading == "QA agent").Link, link);
    }

    private static AiModelStage Stage(string key) => Model.Stages.Single(stage => stage.Key == key);

    private static DocumentDevbookFile File(string name, params DocumentDevbookSection[] sections) =>
        new(name, name, string.Empty, MetadataRecord.Empty, sections);

    private static DocumentDevbookSection Section(
        string heading,
        string status,
        string[]? stage = null,
        string[]? substage = null,
        string[]? layer = null)
    {
        var extra = new Dictionary<string, IReadOnlyList<string>>();
        if (stage is not null) extra["stage"] = stage;
        if (substage is not null) extra["substage"] = substage;
        if (layer is not null) extra["layer"] = layer;

        return new DocumentDevbookSection(heading, $"ai-{heading}", new MetadataRecord { Status = status, Extra = extra }, []);
    }
}
