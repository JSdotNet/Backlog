using Backlog.Desktop.UI.Devbook;
using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The loop picture is read, not drawn: every element of it is one field on a
/// chapter, as <c>devbook-ai.md</c> ("The loop picture") lays out. These tests pin
/// that mapping without a renderer.
/// </summary>
public sealed class AiLoopTests
{
    [Fact]
    public void The_eight_stages_are_the_loop_s_own_in_order_whatever_the_files_say()
    {
        var loop = AiLoop.From([File("01-x.md")]);

        Assert.Equal(
            ["plan", "code", "build", "test", "release", "deploy", "operate", "monitor"],
            loop.Stages.Select(stage => stage.Key));
        Assert.Equal([true, true, true, true, false, false, false, false], loop.Stages.Select(stage => stage.IsDev));
        Assert.All(loop.Stages, stage => Assert.True(stage.IsEmpty));
        Assert.All(loop.Stages, stage => Assert.Null(stage.Status));
    }

    [Fact]
    public void A_chapter_sits_at_every_stage_it_lists_and_nowhere_else()
    {
        var gate = Section("Personal Validation gate", status: "adopted", type: "guardrail", stage: ["test", "release"]);
        var loop = AiLoop.From([File("03-test.md", gate)]);

        Assert.Equal(["Personal Validation gate"], Usages(loop, "test"));
        Assert.Equal(["Personal Validation gate"], Usages(loop, "release"));
        Assert.Empty(Usages(loop, "code"));

        var usage = Assert.Single(Stage(loop, "test").Usages);
        Assert.Equal("adopted", usage.Status);
        Assert.Equal("guardrail", usage.Type);
        Assert.Equal(".ai/03-test.md", usage.Link.Path);
        Assert.Equal("personal-validation-gate", usage.Link.Anchor);
    }

    [Fact]
    public void A_stage_is_shaded_by_the_highest_rung_among_its_usages()
    {
        var loop = AiLoop.From([
            File("04.md",
                Section("Scheduled routines", status: "candidate", stage: ["monitor"]),
                Section("Alerts", status: "trial", stage: ["monitor"]),
                Section("Old bot", status: "retired", stage: ["monitor"]),
                Section("Paused", status: "hold", stage: ["operate"]),
                Section("Retired thing", status: "retired", stage: ["operate"]))
        ]);

        Assert.Equal("trial", Stage(loop, "monitor").Status);
        Assert.Equal("hold", Stage(loop, "operate").Status);
    }

    [Fact]
    public void The_technology_is_what_depends_on_points_at_and_never_related()
    {
        var section = Section("Import plans", status: "adopted", stage: ["plan"]) with
        {
            Meta = new MetadataRecord
            {
                Status = "adopted",
                Extra = new Dictionary<string, IReadOnlyList<string>> { ["stage"] = ["plan"] },
                DependsOn = [DevbookReference.Parse(".devbook/tech/ai-development.md#agent-skills")!],
                Related = [DevbookReference.Parse(".devbook/tech/ai-development.md#subagents")!]
            }
        };

        var usage = Assert.Single(Stage(AiLoop.From([File("01.md", section)]), "plan").Usages);

        Assert.Equal(["agent skills"], usage.Technologies);
    }

    [Fact]
    public void A_concept_with_no_stage_sits_in_the_middle_and_a_chapter_off_the_vocabulary_nowhere()
    {
        var loop = AiLoop.From([
            File("concepts.md",
                Section("Task-scoped context", status: "adopted", type: "concept"),
                Section("Human in the loop", status: "adopted", type: "concept", stage: ["test"])),
            File("adoption-map.md", Section("The loop")),
            File("09.md", Section("Specify skills", status: "trial", type: "skill", stage: ["specify"]))
        ]);

        Assert.Equal(["Task-scoped context"], loop.Concepts.Select(usage => usage.Heading));
        Assert.Equal(["Human in the loop"], Usages(loop, "test"));
        Assert.DoesNotContain(loop.Stages, stage => stage.Usages.Any(usage => usage.Heading == "Specify skills"));
    }

    private static IEnumerable<string> Usages(AiLoop loop, string stage) =>
        Stage(loop, stage).Usages.Select(usage => usage.Heading);

    private static AiLoopStage Stage(AiLoop loop, string key) =>
        loop.Stages.Single(stage => stage.Key == key);

    private static DocumentDevbookFile File(string name, params DocumentDevbookSection[] sections) =>
        new(name, name, string.Empty, MetadataRecord.Empty, sections);

    private static DocumentDevbookSection Section(string heading, string? status = null, string? type = null, string[]? stage = null) =>
        new(
            heading,
            $"ai-{heading}",
            status is null && type is null && stage is null
                ? MetadataRecord.Empty
                : new MetadataRecord
                {
                    Status = status,
                    Type = type,
                    Extra = stage is null
                        ? new Dictionary<string, IReadOnlyList<string>>()
                        : new Dictionary<string, IReadOnlyList<string>> { ["stage"] = stage }
                },
            []);
}
