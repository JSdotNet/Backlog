using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.UI.Components.Feedback;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// A delivery run's stages as the two diagram sources the fold can draw: an Archify
/// specification, which the generator turns into the animated artifact, and a mermaid
/// flowchart, which is what the renderer switch beside it draws.
/// <para>
/// Archify's <c>architecture</c> type rather than its <c>workflow</c> type, which reads
/// like the obvious fit and is not: a workflow lays nodes out on at most six columns,
/// and a run has eleven stages on an ordinary day. What the fold asks for is every
/// stage on one line, which only free placement gives — so every component is placed
/// explicitly, one row, left to right in run order. Grid mode would do the same up to
/// twelve and then stop.
/// </para>
/// <para>
/// The component type carries the tone, because it is the one thing Archify colours
/// by, and the legend is relabelled to say so: a reader sees "Done" and "In progress"
/// in the key rather than "Backend" and "Frontend". The tone is also written into each
/// stage's sublabel, so colour is never the only carrier of it.
/// </para>
/// <para>
/// Deterministic on purpose: the same run gives byte-for-byte the same specification,
/// because the renderer caches the artifact by a hash of it. Nothing that changes
/// without the stages changing — the clock, the token figures — goes in.
/// </para>
/// </summary>
internal static class DeliveryRunDiagramSpec
{
    /// <summary>Archify's own estimate of a label's width per character; a label
    /// wider than its box fails validation rather than being clipped. Rounded up a
    /// little, so an estimate that disagrees by a pixel does not fail a render.</summary>
    private const double LabelPixelsPerCharacter = 7.0;

    /// <summary>The same for a sublabel at the smallest size Archify will shrink one
    /// to before refusing it.</summary>
    private const double SublabelPixelsPerCharacter = 4.0;

    private const int MinimumNodeWidth = 96;
    private const int NodePadding = 20;
    private const int NodeHeight = 64;

    /// <summary>Just over Archify's 24px minimum for a connection, so there is an
    /// arrow to see without the gaps adding up to more than the boxes.</summary>
    private const int Gap = 30;
    private const int Margin = 24;
    private const int Top = 56;

    /// <summary>The Archify specification for <paramref name="run"/>'s stages, as
    /// indented JSON. Only meaningful for a run that has stages.</summary>
    public static string Archify(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var stages = run.Stages;

        // Each box as wide as its own name, not as wide as the longest one. The
        // artifact is scaled to fit the panel, so every pixel of width a box does
        // not need is legibility taken from all of them: sized to the widest, an
        // eleven-stage run with one long stage name drew its labels about 5px tall.
        var components = new JsonArray();
        var connections = new JsonArray();
        var x = Margin;

        for (var index = 0; index < stages.Count; index++)
        {
            var stage = stages[index];
            var width = Width(stage);

            components.Add(new JsonObject
            {
                ["id"] = Id(index),
                ["type"] = ComponentType(DeliveryRunLine.Tone(stage.Status)),
                ["label"] = stage.Name,
                ["sublabel"] = Sublabel(stage),
                ["pos"] = new JsonArray(x, Top),
                ["size"] = new JsonArray(width, NodeHeight)
            });

            x += width + Gap;

            if (index == 0) continue;

            var connection = new JsonObject
            {
                ["id"] = $"c{index - 1}",
                ["from"] = Id(index - 1),
                ["to"] = Id(index)
            };

            // Where the run is heading, emphasised; where it has not been yet,
            // dashed; everything already walked, plain. The trace runs along all of
            // it either way.
            var variant = DeliveryRunLine.Tone(stage.Status) switch
            {
                FlowStepTone.Active => "emphasis",
                FlowStepTone.Blocked => "security",
                FlowStepTone.Pending or FlowStepTone.Skipped => "dashed",
                _ => null
            };

            if (variant is not null) connection["variant"] = variant;

            connections.Add(connection);
        }

        var done = stages.Count(stage => DeliveryRunLine.Tone(stage.Status) is FlowStepTone.Done);

        var spec = new JsonObject
        {
            ["schema_version"] = 1,
            ["diagram_type"] = "architecture",
            ["meta"] = new JsonObject
            {
                ["title"] = string.IsNullOrWhiteSpace(run.Title) ? run.SkillId : run.Title,
                ["subtitle"] = $"{run.SkillId} · {done} of {stages.Count} stages done",
                ["animation"] = "trace",
                ["quality_profile"] = "standard",
                ["viewBox"] = new JsonArray(
                    Math.Max(320, x - Gap + Margin),
                    Math.Max(240, Top + NodeHeight + 72)),
                ["legend"] = new JsonObject
                {
                    ["mode"] = "auto",
                    ["entries"] = new JsonObject
                    {
                        ["backend"] = new JsonObject { ["label"] = "Done" },
                        ["frontend"] = new JsonObject { ["label"] = "In progress" },
                        ["security"] = new JsonObject { ["label"] = "Blocked" },
                        ["external"] = new JsonObject { ["label"] = "Not reached" }
                    }
                }
            },
            ["components"] = components,
            ["connections"] = connections
        };

        return spec.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The same stages as a mermaid flowchart: what the Mermaid side of the renderer
    /// switch draws, so a reader can check the artifact against a drawing of the same
    /// facts made without Archify.
    /// </summary>
    public static string Mermaid(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var builder = new StringBuilder("flowchart LR");

        for (var index = 0; index < run.Stages.Count; index++)
        {
            var stage = run.Stages[index];
            var node = $"{Id(index)}[\"{Escape(stage.Name)}<br/>{Escape(DeliveryRunLine.StatusLabel(stage.Status))}\"]";

            builder.Append('\n').Append("    ");
            builder.Append(index == 0 ? node : $"{Id(index - 1)} --> {node}");
        }

        return builder.ToString();
    }

    private static string Id(int index) => $"s{index}";

    private static int Width(DeliveryRunStage stage) =>
        Math.Max(
            MinimumNodeWidth,
            (int)Math.Ceiling(Math.Max(
                stage.Name.Length * LabelPixelsPerCharacter,
                Sublabel(stage).Length * SublabelPixelsPerCharacter)) + NodePadding);

    /// <summary>The tone as the component type Archify colours by; the legend
    /// relabels each of the four used.</summary>
    private static string ComponentType(FlowStepTone tone) => tone switch
    {
        FlowStepTone.Done => "backend",
        FlowStepTone.Active => "frontend",
        FlowStepTone.Blocked => "security",
        _ => "external"
    };

    /// <summary>
    /// Where the stage stands, then who worked in it: the one agent it delegated to
    /// where there was one — with a count of the rest — and the owner session
    /// otherwise. A stage nobody has reached names nobody, as the flow beside it
    /// does. The models stay in the fold's figures; a model id is the longest thing
    /// a box could carry and the least read.
    /// </summary>
    private static string Sublabel(DeliveryRunStage stage)
    {
        var status = DeliveryRunLine.StatusLabel(stage.Status);

        if (DeliveryRunLine.Tone(stage.Status) is FlowStepTone.Pending or FlowStepTone.Skipped) return status;

        var worker = stage.Agents.Count switch
        {
            0 => "main session",
            1 => stage.Agents[0].Name,
            var count => $"{stage.Agents[0].Name} +{count - 1}"
        };

        return $"{status} · {worker}";
    }

    /// <summary>A mermaid label is a quoted string, so a quote in a stage name would
    /// end it early.</summary>
    private static string Escape(string text) => text.Replace("\"", "#quot;", StringComparison.Ordinal);
}
