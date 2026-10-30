using Backlog.Desktop.UI.Devbook;
using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The AI model is read, not drawn: every element of it is one field on a
/// chapter, as <c>devbook-ai.md</c> ("The loop picture") lays out. These tests pin
/// that mapping without a renderer, on fixtures rather than the repository's own
/// chapters.
/// </summary>
public sealed class AiModelTests
{
    [Fact]
    public void The_seven_stages_are_the_path_s_own_in_order_whatever_the_files_say()
    {
        var model = AiModel.From([File("01-x.md")]);

        Assert.Equal(
            ["decide", "specify", "code", "verify", "integrate", "release", "operate"],
            model.Stages.Select(stage => stage.Key));
        Assert.Equal(
            ["Decide", "Specify & plan", "Code", "Verify", "Integrate", "Release & deploy", "Operate & feedback"],
            model.Stages.Select(stage => stage.Name));
        Assert.Equal([true, true, true, true, false, false, false], model.Stages.Select(stage => stage.BeforePullRequest));
        Assert.All(model.Stages, stage => Assert.True(stage.IsEmpty));
        Assert.All(model.Stages, stage => Assert.Null(stage.Status));
        Assert.All(model.Stages, stage => Assert.Empty(stage.Substages));
    }

    [Fact]
    public void The_three_layers_are_always_there_and_hold_the_chapters_listing_them()
    {
        var model = AiModel.From([
            File("05.md",
                Section("Worktree per change", status: "adopted", layer: ["harness"]),
                Section("Sync sweep", status: "trial", stage: ["verify"], layer: ["thread", "harness"]),
                Section("Typo layer", status: "trial", layer: ["plumbing"]))
        ]);

        Assert.Equal(["thread", "upkeep", "harness"], model.Layers.Select(layer => layer.Key));
        Assert.Equal(["Spec ↔ code thread", "Upkeep loop", "Agent harness"], model.Layers.Select(layer => layer.Name));
        Assert.Equal(["Sync sweep"], Headings(Layer(model, "thread").Usages));
        Assert.True(Layer(model, "upkeep").IsEmpty);
        Assert.Null(Layer(model, "upkeep").Status);
        Assert.Equal(["Worktree per change", "Sync sweep"], Headings(Layer(model, "harness").Usages));
        Assert.Equal("adopted", Layer(model, "harness").Status);

        // A word off the vocabulary places the chapter nowhere; it is still a usage.
        var typo = Assert.Single(model.Usages, usage => usage.Heading == "Typo layer");
        Assert.Empty(typo.Layers);
        Assert.DoesNotContain(model.Layers, layer => layer.Usages.Contains(typo));
    }

    [Fact]
    public void A_chapter_sits_at_every_stage_it_lists_and_nowhere_else()
    {
        var gate = Section("Personal Validation gate", status: "adopted", type: "guardrail", stage: ["verify", "integrate", "monitor"]);
        var model = AiModel.From([File("03-verify.md", gate)]);

        Assert.Equal(["Personal Validation gate"], Headings(Stage(model, "verify").Usages));
        Assert.Equal(["Personal Validation gate"], Headings(Stage(model, "integrate").Usages));
        Assert.Empty(Stage(model, "code").Usages);

        // `monitor` is the retired eight-stage word: off the vocabulary, dropped.
        Assert.Equal(["verify", "integrate"], Assert.Single(model.Usages).Stages);
    }

    [Fact]
    public void A_usage_carries_every_field_the_views_read()
    {
        var section = Section(
            "Merge review",
            status: "trial",
            type: "skill",
            stage: ["verify", "integrate"],
            substage: ["integrate/review", "verify/review"],
            layer: ["upkeep"],
            evidence: "Partial",
            date: "2026-09-01",
            dependsOn: [".devbook/tech/ai-development.md#delivery-engine"],
            body:
            [
                new DocumentDevbookParagraph([new MdText("Reviews every open pull request.")]),
                new DocumentDevbookList(false,
                [
                    [new MdStrong("Used for"), new MdText(" — the merge verdict.")],
                    [new MdStrong("Moves up when"), new MdText(" — a verdict blocks a "), new MdCodeSpan("main"), new MdText(" merge.")],
                    [new MdStrong("Limits"), new MdText(" — never approves.")]
                ])
            ]);

        var usage = Assert.Single(AiModel.From([File("04-integrate.md", section)]).Usages);

        Assert.Equal("Merge review", usage.Heading);
        Assert.Equal("04-integrate.md", usage.FileName);
        Assert.Equal("skill", usage.Type);
        Assert.Equal("trial", usage.Status);
        Assert.Equal("partial", usage.Evidence);
        Assert.Equal("a verdict blocks a main merge.", usage.MovesUpWhen);
        Assert.Equal(["verify", "integrate"], usage.Stages);
        Assert.Equal(["integrate/review", "verify/review"], usage.Substages);
        Assert.Equal(["upkeep"], usage.Layers);
        Assert.Equal([".devbook/tech/ai-development.md#delivery-engine"], usage.DependsOn.Select(reference => reference.Raw));
        Assert.Equal(["delivery engine"], usage.Technologies);
        Assert.Equal("2026-09-01", usage.Date);
        Assert.Equal("ai", usage.Link.AreaKey);
        Assert.EndsWith("ai/04-integrate.md", usage.Link.Path, StringComparison.Ordinal);
        Assert.Equal("merge-review", usage.Link.Anchor);
    }

    [Fact]
    public void A_usage_without_the_optional_fields_reads_them_as_absent()
    {
        var usage = Assert.Single(AiModel.From([File("01.md", Section("Plain", status: "adopted", stage: ["code"]))]).Usages);

        Assert.Null(usage.Evidence);
        Assert.Null(usage.MovesUpWhen);
        Assert.Null(usage.Type);
        Assert.Empty(usage.Substages);
        Assert.Empty(usage.Layers);
        Assert.Empty(usage.Technologies);
    }

    [Fact]
    public void Sub_stages_follow_their_first_appearance_across_the_files_and_need_their_stage()
    {
        var model = AiModel.From([
            File("01.md",
                Section("Unit tests", status: "adopted", stage: ["verify"], substage: ["verify/build-test"]),
                Section("Reviewer", status: "trial", stage: ["verify"], substage: ["verify/review"])),
            File("02.md",
                Section("QA agent", status: "trial", stage: ["verify"], substage: ["verify/functional-qa", "verify/build-test"]),
                Section("Stray", status: "trial", stage: ["code"], substage: ["verify/acceptance", "code/implement"]))
        ]);

        var verify = Stage(model, "verify");
        Assert.Equal(["verify/build-test", "verify/review", "verify/functional-qa"], verify.Substages.Select(substage => substage.Key));
        Assert.Equal(["build-test", "review", "functional-qa"], verify.Substages.Select(substage => substage.Word));
        Assert.Equal(["Unit tests", "QA agent"], Headings(verify.Substages[0].Usages));

        // `verify/acceptance` names a stage the chapter does not list: ignored.
        Assert.Equal(["code/implement"], Stage(model, "code").Substages.Select(substage => substage.Key));
        Assert.Equal(["code/implement"], Assert.Single(model.Usages, usage => usage.Heading == "Stray").Substages);
    }

    [Fact]
    public void A_stage_is_shaded_by_the_highest_rung_among_its_usages()
    {
        var model = AiModel.From([
            File("04.md",
                Section("Scheduled routines", status: "candidate", stage: ["operate"]),
                Section("Alerts", status: "trial", stage: ["operate"]),
                Section("Old bot", status: "retired", stage: ["operate"]),
                Section("Paused", status: "hold", stage: ["release"]),
                Section("Retired thing", status: "retired", stage: ["release"]))
        ]);

        Assert.Equal("trial", Stage(model, "operate").Status);
        Assert.Equal("hold", Stage(model, "release").Status);
        Assert.Null(Stage(model, "decide").Status);
        Assert.True(Stage(model, "decide").IsEmpty);
    }

    [Fact]
    public void A_feed_resolves_to_the_usage_it_names_with_its_label()
    {
        var model = AiModel.From([
            File("01-specify.md",
                Section("Import plans", status: "adopted", stage: ["specify"],
                    feeds: [".devbook/ai/02-code.md#code-gate: the agreed plan, step by step", "02-code.md#code-gate: again, relative"])),
            File("02-code.md", Section("Code gate", status: "adopted", stage: ["code"]))
        ]);

        Assert.Empty(model.UnresolvedFeeds);
        Assert.Equal(2, model.Feeds.Count);

        var feed = model.Feeds[0];
        Assert.True(feed.IsResolved);
        Assert.Equal("Import plans", feed.From.Heading);
        Assert.Equal("Code gate", feed.To!.Heading);
        Assert.Equal("the agreed plan, step by step", feed.Label);
        Assert.Equal(".devbook/ai/02-code.md#code-gate", feed.Target);
        Assert.Equal("Code gate", model.Feeds[1].To!.Heading);
    }

    [Fact]
    public void An_unresolved_feed_is_reported_rather_than_dropped()
    {
        var model = AiModel.From([
            File("01.md",
                Section("Hand-off", status: "trial", stage: ["code"],
                    feeds:
                    [
                        ".devbook/ai/09-gone.md#nowhere: a lost hand-off",
                        ".devbook/tech/tooling.md#claude-code: not a usage",
                        ".devbook/ai/01.md#receiver",
                        ".devbook/ai/01.md#receiver: fine"
                    ]),
                Section("Receiver", status: "trial", stage: ["verify"]))
        ]);

        Assert.Equal(["Receiver"], model.Feeds.Select(feed => feed.To!.Heading));
        Assert.Equal(3, model.UnresolvedFeeds.Count);
        Assert.All(model.UnresolvedFeeds, feed => Assert.False(feed.IsResolved));
        Assert.All(model.UnresolvedFeeds, feed => Assert.Equal("Hand-off", feed.From.Heading));

        Assert.Equal(".devbook/ai/09-gone.md#nowhere", model.UnresolvedFeeds[0].Target);
        Assert.Equal("a lost hand-off", model.UnresolvedFeeds[0].Label);
        Assert.Null(model.UnresolvedFeeds[0].To);
        Assert.Equal(".devbook/tech/tooling.md#claude-code", model.UnresolvedFeeds[1].Target);

        // No label: the entry is not a feed, and is reported whole.
        Assert.Equal(".devbook/ai/01.md#receiver", model.UnresolvedFeeds[2].Target);
        Assert.Null(model.UnresolvedFeeds[2].Label);
    }

    [Fact]
    public void Related_links_are_support_edges_resolved_where_they_name_a_usage()
    {
        var model = AiModel.From([
            File("01.md",
                Section("Reviewer", status: "trial", stage: ["verify"],
                    related: [".devbook/ai/02.md#qa-agent", ".devbook/ai/concepts.md#context-engineering"])),
            File("02.md", Section("QA agent", status: "trial", stage: ["verify"]))
        ]);

        Assert.Equal(2, model.Supports.Count);
        Assert.All(model.Supports, support => Assert.Equal("Reviewer", support.From.Heading));
        Assert.Equal("QA agent", model.Supports[0].To!.Heading);
        Assert.Null(model.Supports[1].To);
        Assert.Equal(".devbook/ai/concepts.md#context-engineering", model.Supports[1].Target.Raw);

        // related is a reading hint, not a hand-off and not a dependency.
        Assert.Empty(model.Feeds);
        Assert.Empty(model.Usages[0].Technologies);
    }

    [Fact]
    public void A_concept_with_neither_stage_nor_layer_applies_throughout_and_prose_is_no_usage()
    {
        var model = AiModel.From([
            File("concepts.md",
                Section("Task-scoped context", status: "adopted", type: "concept"),
                Section("Human in the loop", status: "adopted", type: "concept", stage: ["verify"])),
            File("adoption-map.md", Section("The loop")),
            File("09.md", Section("Unplaced practice", status: "trial", type: "practice"))
        ]);

        Assert.Equal(["Task-scoped context"], Headings(model.Throughout));
        Assert.Equal(["Human in the loop"], Headings(Stage(model, "verify").Usages));
        Assert.Equal(["Task-scoped context", "Human in the loop", "Unplaced practice"], Headings(model.Usages));
        Assert.DoesNotContain(model.Stages, stage => stage.Usages.Any(usage => usage.Heading == "Unplaced practice"));
    }

    [Fact]
    public void A_block_as_authored_reads_through_the_metadata_reader_with_a_comma_in_a_quoted_label()
    {
        var meta = MetadataReader.Parse("""
            status: trial
            type: practice
            stage: [code, verify]
            substage: [verify/review]
            layer: [thread]
            feeds: [".devbook/ai/02.md#specify--plan-review: the plan, step by step", "02.md#what-s-next: a, b"]
            evidence: recorded
            depends-on: [".devbook/tech/tooling.md#claude-code"]
            related: [".ai/02.md#whats-next"]
            date: 2026-09-01
            """);
        var source = new DocumentDevbookSection("Author", "ai-author", meta,
        [
            new DocumentDevbookList(false, [[new MdStrong("Moves up when:"), new MdText(" two runs land.")]])
        ]);

        var model = AiModel.From([
            File("01.md", source),
            File("02.md",
                Section("Specify & plan review", status: "adopted", stage: ["specify"]),
                Section("What's next", status: "adopted", stage: ["decide"]))
        ]);

        var usage = model.Usages[0];
        Assert.Equal(["code", "verify"], usage.Stages);
        Assert.Equal(["verify/review"], usage.Substages);
        Assert.Equal(["thread"], usage.Layers);
        Assert.Equal("recorded", usage.Evidence);
        Assert.Equal("two runs land.", usage.MovesUpWhen);
        Assert.Equal(["claude code"], usage.Technologies);

        // GitHub's slug for a heading with punctuation resolves as well as the
        // app's own, and the quoted labels keep their commas.
        Assert.Empty(model.UnresolvedFeeds);
        Assert.Equal(["Specify & plan review", "What's next"], model.Feeds.Select(feed => feed.To!.Heading));
        Assert.Equal(["the plan, step by step", "a, b"], model.Feeds.Select(feed => feed.Label));
        Assert.Equal("What's next", Assert.Single(model.Supports).To!.Heading);
    }

    [Fact]
    public void A_chapter_with_neither_stage_nor_layer_that_is_no_concept_is_reported_unplaced()
    {
        var model = AiModel.From([
            File("01.md",
                Section("Lost practice", status: "trial", type: "practice", stage: ["monitor"]),
                Section("Concept", status: "adopted", type: "concept"),
                Section("Placed", status: "trial", stage: ["code"],
                    feeds: ["01.md#placed: self", "01.md#placed: self"],
                    related: ["01.md#concept", "01.md#concept"]))
        ]);

        Assert.Equal(["Lost practice"], Headings(model.Unplaced));
        Assert.Equal(["Concept"], Headings(model.Throughout));

        // A repeated entry is one edge.
        Assert.Single(model.Feeds);
        Assert.Single(model.Supports);
    }

    private static IEnumerable<string> Headings(IEnumerable<AiModelUsage> usages) =>
        usages.Select(usage => usage.Heading);

    private static AiModelStage Stage(AiModel model, string key) =>
        model.Stages.Single(stage => stage.Key == key);

    private static AiModelLayer Layer(AiModel model, string key) =>
        model.Layers.Single(layer => layer.Key == key);

    private static DocumentDevbookFile File(string name, params DocumentDevbookSection[] sections) =>
        new(name, name, string.Empty, MetadataRecord.Empty, sections);

    private static DocumentDevbookSection Section(
        string heading,
        string? status = null,
        string? type = null,
        string[]? stage = null,
        string[]? substage = null,
        string[]? layer = null,
        string[]? feeds = null,
        string? evidence = null,
        string? date = null,
        string[]? dependsOn = null,
        string[]? related = null,
        DocumentDevbookBlock[]? body = null)
    {
        var extra = new Dictionary<string, IReadOnlyList<string>>();
        if (stage is not null) extra["stage"] = stage;
        if (substage is not null) extra["substage"] = substage;
        if (layer is not null) extra["layer"] = layer;
        if (feeds is not null) extra["feeds"] = feeds;
        if (evidence is not null) extra["evidence"] = [evidence];

        var meta = status is null && type is null && extra.Count == 0
            ? MetadataRecord.Empty
            : new MetadataRecord
            {
                Status = status,
                Type = type,
                Date = date,
                Extra = extra,
                DependsOn = [.. (dependsOn ?? []).Select(raw => DevbookReference.Parse(raw)!)],
                Related = [.. (related ?? []).Select(raw => DevbookReference.Parse(raw)!)]
            };

        return new DocumentDevbookSection(heading, $"ai-{heading}", meta, body ?? []);
    }
}
