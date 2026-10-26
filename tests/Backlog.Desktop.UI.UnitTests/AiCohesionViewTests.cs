using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Metadata;
using Backlog.UI.Components.Devbook;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Cohesion tab draws <see cref="AiModel"/>, laid out by
/// <see cref="AiCohesionLayout"/>, on the library's cohesion board: these tests
/// pin the translation — a usage addressed by its chapter, its
/// <c>depends-on</c> the platforms it is built on, scheduled when it depends on
/// the delivery schedule, the layout's edges and coordinates carried over — and
/// that opening a usage hands its own chapter to the host.
/// </summary>
public sealed class AiCohesionViewTests
{
    private const string Engine = ".devbook/tech/ai-development.md#delivery-engine";
    private const string Schedule = ".devbook/tech/ai-development.md#delivery-schedule";

    private static readonly AiModel Model = AiModel.From([
        File("01-plan.md",
            Section("Spec", "adopted", stage: ["specify"], feeds: ["02-code.md#gate: apply-change"], dependsOn: [Engine])),
        File("02-code.md",
            Section("Gate", "adopted", stage: ["code"], dependsOn: [Engine, Engine.ToUpperInvariant()]),
            Section("Worktree", "adopted", layer: ["harness"], related: [".devbook/ai/02-code.md#gate"])),
        File("05-unattended-runs.md",
            Section("Upkeep", "trial", layer: ["upkeep"], feeds: ["02-code.md#gate: pull requests"], dependsOn: [Schedule]),
            Section("Alerts", "candidate", stage: ["operate"], feeds: ["01-plan.md#spec: would enter Decide"], dependsOn: [Engine]),
            Section("Notes", "adopted"))
    ]);

    [Fact]
    public void Every_placed_usage_becomes_a_node_at_the_layout_s_box_addressed_by_its_chapter()
    {
        var (graph, byId) = AiCohesionView.Graph(Model);
        var layout = AiCohesionLayout.From(Model);

        Assert.Equal(layout.Nodes.Count, graph.Nodes.Count);
        Assert.Equal(["Spec", "Gate", "Worktree", "Upkeep", "Alerts"], graph.Nodes.Select(node => node.Name));

        var gate = graph.Nodes.Single(node => node.Name == "Gate");
        var laid = layout.Nodes.Single(node => node.Usage.Heading == "Gate");
        Assert.Equal((laid.X, laid.Y, laid.Width, laid.Height), (gate.X, gate.Y, gate.Width, gate.Height));
        Assert.Equal(3, gate.Stage);
        Assert.True(gate.OnPath);
        Assert.Equal("Code", gate.Where);
        Assert.Same(byId[gate.Id], laid.Usage);
        Assert.Contains("02-code.md", gate.Id);

        var worktree = graph.Nodes.Single(node => node.Name == "Worktree");
        Assert.False(worktree.OnPath);
        Assert.Equal("Agent harness", worktree.Where);

        Assert.Equal(["decide", "specify", "code", "verify", "integrate", "release", "operate"], graph.Lanes.Select(lane => lane.Key));
        Assert.Equal(["thread", "upkeep", "harness"], graph.Bands.Select(band => band.Key));
        Assert.Equal(layout.PullRequestX, graph.PullRequestX);
    }

    [Fact]
    public void A_usage_that_depends_on_the_delivery_schedule_is_scheduled_and_its_depends_on_are_its_platforms()
    {
        var (graph, _) = AiCohesionView.Graph(Model);

        Assert.Equal(["Upkeep"], graph.Nodes.Where(node => node.Scheduled).Select(node => node.Name));

        // One platform per chapter addressed, however it is spelled.
        Assert.Equal(["Delivery engine", "Delivery schedule"], graph.Platforms.Select(platform => platform.Name));
        Assert.Single(graph.Nodes.Single(node => node.Name == "Gate").Platforms);
        Assert.Equal(3, graph.UsageCount(graph.Platforms[0].Id));
    }

    [Fact]
    public void The_layout_s_edges_carry_over_with_their_kind_dash_label_and_route()
    {
        var (graph, _) = AiCohesionView.Graph(Model);
        var layout = AiCohesionLayout.From(Model);
        string Name(string id) => graph.Node(id)!.Name;

        Assert.Equal(
            ["Spec>Gate:Feed", "Upkeep>Gate:Feed", "Alerts>Spec:OpenLoop", "Worktree>Gate:Support"],
            graph.Edges.Select(edge => $"{Name(edge.From)}>{Name(edge.To)}:{edge.Kind}"));

        var loop = graph.Edges.Single(edge => edge.Kind == CohesionEdgeKind.OpenLoop);
        var laid = layout.Edges.Single(edge => edge.Kind == AiCohesionEdgeKind.OpenLoop);
        Assert.True(loop.IsDashed);
        Assert.Equal("would enter Decide", loop.Label);
        Assert.Equal(laid.Points.Select(point => (point.X, point.Y)), loop.Points.Select(point => (point.X, point.Y)));

        Assert.True(graph.IsForward(graph.Edges[0]));
        Assert.True(graph.FeedsBack(graph.Edges[1]));
        Assert.True(graph.FeedsBack(graph.Edges[2]));
    }

    [Fact]
    public void Opening_a_picked_usage_hands_its_own_chapter_to_the_host()
    {
        using var context = new BunitContext();
        DevbookChapterLink? opened = null;
        var view = context.Render<AiCohesionView>(parameters => parameters
            .Add(p => p.Model, Model)
            .Add(p => p.OnNavigateToChapter, (DevbookChapterLink link) => opened = link));

        var gate = view.FindAll("[data-testid='ai-cohesion-board-node']").Single(node => node.GetAttribute("aria-label")!.StartsWith("Gate,", StringComparison.Ordinal));
        Assert.Equal("Gate, adopted, Code", gate.GetAttribute("aria-label"));
        gate.Click();
        view.Find("[data-testid='ai-cohesion-board-open']").Click();

        Assert.NotNull(opened);
        Assert.Equal(Model.Usages.Single(usage => usage.Heading == "Gate").Link, opened);
    }

    [Fact]
    public void A_record_with_nothing_placed_says_so()
    {
        using var context = new BunitContext();
        var empty = AiModel.From([File("01.md", Section("Concept", "adopted"))]);

        var view = context.Render<AiCohesionView>(parameters => parameters.Add(p => p.Model, empty));

        Assert.Contains("Nothing to connect yet", view.Markup);
        Assert.Empty(view.FindAll("[data-testid='ai-cohesion-board']"));
    }

    private static DocumentDevbookFile File(string name, params DocumentDevbookSection[] sections) =>
        new(name, name, string.Empty, MetadataRecord.Empty, sections);

    private static DocumentDevbookSection Section(
        string heading,
        string status,
        string[]? stage = null,
        string[]? layer = null,
        string[]? feeds = null,
        string[]? dependsOn = null,
        string[]? related = null)
    {
        var extra = new Dictionary<string, IReadOnlyList<string>>();
        if (stage is not null) extra["stage"] = stage;
        if (layer is not null) extra["layer"] = layer;
        if (feeds is not null) extra["feeds"] = [.. feeds.Select(feed => ".devbook/ai/" + feed)];

        var meta = new MetadataRecord
        {
            Status = status,
            Type = "skill",
            Extra = extra,
            DependsOn = [.. (dependsOn ?? []).Select(raw => DevbookReference.Parse(raw)!)],
            Related = [.. (related ?? []).Select(raw => DevbookReference.Parse(raw)!)]
        };
        return new DocumentDevbookSection(heading, $"ai-{heading.ToLowerInvariant()}", meta, []);
    }
}
