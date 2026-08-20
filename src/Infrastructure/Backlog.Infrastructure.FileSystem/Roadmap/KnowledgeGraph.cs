using System.Globalization;
using System.Text.Json;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// One node of the generated knowledge graph, reduced to what a roadmap rollup
/// reads: what it is called, the effort it registered, and the roadmap item tags it
/// contributes to.
/// </summary>
/// <param name="Id">The chapter reference, <c>&lt;path&gt;#&lt;slug&gt;</c> — the
/// same opaque form a roadmap item's <c>KnowledgeRefs</c> use, so a direct
/// reference and a node line up by string.</param>
/// <param name="Label">What to show for the chapter.</param>
/// <param name="Effort">The story points its <c>meta</c> block registered, parsed
/// to a non-negative integer, or <see langword="null"/> when it registered none or
/// registered something unreadable — this side is a reader.</param>
/// <param name="Roadmap">The roadmap item tag slugs its <c>meta</c> block names.</param>
public sealed record KnowledgeGraphNode(string Id, string Label, int? Effort, IReadOnlyList<string> Roadmap);

/// <summary>
/// Reads the nodes out of the generated <c>_meta/graph.json</c>.
/// <para>
/// The graph is the one source that already carries every folder's chapters with
/// their parsed <c>roadmap</c> and <c>effort</c> together, so a rollup reads it
/// rather than re-scanning the markdown. Parsing is a pure function of the text so
/// it can be asserted without a file behind it; missing or malformed input reads as
/// no nodes rather than throwing, because a graph that has not been generated yet is
/// an ordinary state.
/// </para>
/// </summary>
public static class KnowledgeGraph
{
    public static IReadOnlyList<KnowledgeGraphNode> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("elements", out var elements)
                || elements.ValueKind != JsonValueKind.Object
                || !elements.TryGetProperty("nodes", out var nodes)
                || nodes.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<KnowledgeGraphNode>();

            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object
                    || !node.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = String(data, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var label = String(data, "label");
                result.Add(new KnowledgeGraphNode(
                    id,
                    string.IsNullOrWhiteSpace(label) ? id : label,
                    Effort(data),
                    Strings(data, "roadmap")));
            }

            return result;
        }
    }

    private static string? String(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Effort(JsonElement data)
    {
        if (!data.TryGetProperty("effort", out var value)) return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var points) && points >= 0
            ? points
            : null;
    }

    private static IReadOnlyList<string> Strings(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return [];

        var values = new List<string>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String) continue;
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text)) values.Add(text);
        }

        return values;
    }
}
