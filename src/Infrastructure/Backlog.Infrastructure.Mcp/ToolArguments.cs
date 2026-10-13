using System.Text.Json;

using Backlog.SharedKernel.Results;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The delivery surface's arguments, read against the tool's own input schema
/// before the SDK binds them.
/// <para>
/// <b>Why before.</b> The SDK answers every exception but an
/// <see cref="McpException"/> with "An error occurred invoking '&lt;tool&gt;'." and
/// nothing else, and both ways binding fails are other exceptions: a required
/// argument left out is an <see cref="ArgumentException"/>, an argument of the
/// wrong JSON kind a <see cref="JsonException"/>. An execute-plan worker that sent
/// <c>start_run</c> its stages as a string and no worktree got that sentence back
/// twice and could correct neither. Read here, both are refused by the argument's
/// name, as the one exception whose message the SDK hands the client.
/// </para>
/// <para>
/// <b>Why tolerant of one shape.</b> Agents send a structured argument as the JSON
/// text of it often enough — <c>"stages": "[\"Scope\",…]"</c>, <c>"stageIndex":
/// "3"</c> — that refusing it costs a run its record over a quoting choice. A string
/// sent where the schema asks for anything but a string is parsed, and kept when
/// what it encodes is the kind asked for. Nothing else is coerced: a string that
/// encodes some other kind, or no JSON at all, is refused by name.
/// </para>
/// <para>
/// <b>Why read off the schema.</b> The schema is what <c>tools/list</c> told the
/// caller, built by the SDK from the method's parameters. Reading the same document
/// keeps this check from becoming a second list of parameters to drift from
/// <see cref="SurfaceTools"/>'s signatures.
/// </para>
/// <para>
/// <b>Why only the surface.</b> Its callers are flows, which compose these calls
/// from the engine's contract rather than from the live schema, and lose the run's
/// record when one fails. Every other group keeps the SDK's binding unchanged.
/// </para>
/// </summary>
public static class ToolArguments
{
    /// <summary>
    /// The call-tool filter a host installs: prepares the arguments of a surface tool
    /// the SDK has already matched, and passes every other call on untouched.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            if (request.MatchedPrimitive is McpServerTool tool
                && request.Params is { } call
                && Applies(tool.ProtocolTool.Name))
            {
                call.Arguments = Prepare(tool.ProtocolTool, call.Arguments);
            }

            return await next(request, cancellationToken).ConfigureAwait(false);
        };

    /// <summary>Whether a tool's arguments are prepared here: the delivery surface's,
    /// and no other group's.</summary>
    internal static bool Applies(string toolName) =>
        BacklogMcpTools.Surface.ToolNames.Contains(toolName, StringComparer.Ordinal);

    /// <summary>
    /// The arguments as the binder should see them: a JSON-encoded string decoded
    /// where the schema asks for the kind it encodes, and the call refused by
    /// argument name where a required one is missing or one is of a kind the schema
    /// does not allow. An argument the schema does not name is left for the binder,
    /// which ignores it.
    /// </summary>
    internal static IDictionary<string, JsonElement> Prepare(Tool tool, IDictionary<string, JsonElement>? arguments)
    {
        var prepared = new Dictionary<string, JsonElement>(
            arguments ?? new Dictionary<string, JsonElement>(),
            StringComparer.Ordinal);

        var schema = tool.InputSchema;
        var properties = schema.TryGetProperty("properties", out var named) && named.ValueKind is JsonValueKind.Object
            ? named
            : default;

        if (schema.TryGetProperty("required", out var required) && required.ValueKind is JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(entry => entry.GetString()).OfType<string>())
            {
                var parameter = Parameter(properties, name);

                // A null is "not sent" unless the parameter's own schema allows one.
                if (prepared.TryGetValue(name, out var value)
                    && (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                        || (value.ValueKind is JsonValueKind.Null && Types(parameter).Contains("null"))))
                {
                    continue;
                }

                var needs = parameter.TryGetProperty("description", out var description)
                    && description.GetString() is { Length: > 0 } text
                        ? $"{tool.Name} needs {name} — {char.ToLowerInvariant(text[0])}{text[1..].TrimEnd('.')}."
                        : $"{tool.Name} needs {name}.";

                throw Refusal($"{name}.missing", $"{needs} It was not sent.");
            }
        }

        if (properties.ValueKind is not JsonValueKind.Object) return prepared;

        foreach (var (name, value) in prepared.ToList())
        {
            if (!properties.TryGetProperty(name, out _)) continue;

            prepared[name] = Conform(name, value, Parameter(properties, name));
        }

        return prepared;
    }

    /// <summary>One parameter's schema, or an empty object where there is none or
    /// it is a boolean schema — which says nothing this check could act on.</summary>
    private static JsonElement Parameter(JsonElement properties, string name) =>
        properties.ValueKind is JsonValueKind.Object
        && properties.TryGetProperty(name, out var parameter)
        && parameter.ValueKind is JsonValueKind.Object
            ? parameter
            : EmptySchema;

    private static readonly JsonElement EmptySchema = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>The value as the parameter's schema allows it, decoded from a JSON
    /// string where that is what makes it so, or a refusal naming the argument. An
    /// array's items are held to the items' schema one level down, because that is
    /// where <c>stages</c> breaks: a list with one entry that is not a string.</summary>
    private static JsonElement Conform(string name, JsonElement value, JsonElement parameter)
    {
        var allowed = Types(parameter);

        if (allowed.Count == 0) return value;

        if (!Is(value, allowed)
            && value.ValueKind is JsonValueKind.String
            && !allowed.Contains("string")
            && Decode(value.GetString()) is { } decoded
            && Is(decoded, allowed))
        {
            value = decoded;
        }

        if (!Is(value, allowed))
        {
            throw Refusal(
                $"{name}.wrong-kind",
                $"{name} must be {Describe(allowed)}; it was sent as {Describe(value)}.");
        }

        // A null entry included: the binder would take one, and hand the port a
        // stage with no name.
        if (value.ValueKind is JsonValueKind.Array
            && parameter.TryGetProperty("items", out var items)
            && Types(items) is { Count: > 0 } itemTypes)
        {
            var index = 0;

            foreach (var item in value.EnumerateArray())
            {
                if (!Is(item, itemTypes))
                {
                    throw Refusal(
                        $"{name}.wrong-kind",
                        $"{name}[{index}] must be {Describe(itemTypes)}; it was sent as {Describe(item)}.");
                }

                index++;
            }
        }

        return value;
    }

    /// <summary>The JSON types a schema allows: its <c>type</c>, as one name or a
    /// list of them. None for a schema that does not say, which is how the SDK
    /// describes a <see cref="JsonElement"/> parameter — that one's kind is the
    /// tool's to check.</summary>
    private static HashSet<string> Types(JsonElement schema) =>
        schema.ValueKind is not JsonValueKind.Object || !schema.TryGetProperty("type", out var type) ? []
        : type.ValueKind is JsonValueKind.String ? [type.GetString()!]
        : type.ValueKind is JsonValueKind.Array ? [.. type.EnumerateArray().Select(entry => entry.GetString()).OfType<string>()]
        : [];

    private static bool Is(JsonElement value, HashSet<string> allowed) => value.ValueKind switch
    {
        JsonValueKind.String => allowed.Contains("string"),
        JsonValueKind.Array => allowed.Contains("array"),
        JsonValueKind.Object => allowed.Contains("object"),
        JsonValueKind.True or JsonValueKind.False => allowed.Contains("boolean"),
        JsonValueKind.Null => allowed.Contains("null"),
        JsonValueKind.Number => allowed.Contains("number") || (allowed.Contains("integer") && value.TryGetInt64(out _)),
        _ => false
    };

    private static JsonElement? Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Describe(HashSet<string> allowed) =>
        string.Join(" or ", allowed.Where(type => type != "null").Order(StringComparer.Ordinal).Select(Article));

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Decode(value.GetString()) is { } decoded
            ? $"a string holding {Describe(decoded)}"
            : "a string",
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null"
    };

    private static string Article(string type) => type switch
    {
        "array" => "an array",
        "object" => "an object",
        "integer" => "an integer",
        _ => $"a {type}"
    };

    private static McpException Refusal(string code, string message) =>
        RepositoryScope.Failure(Error.Validation(code, message));
}
