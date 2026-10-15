using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Backlog.EndToEndTests;

/// <summary>
/// The desktop harness's own MCP endpoint, <c>/mcp</c>, as a caller of its tools —
/// what a delivery run and an agent filing work reach the app through, so a test
/// that needs a run or a task's references puts them there the same way.
///
/// <para>A dozen lines of JSON-RPC over the streamable HTTP transport rather than a
/// client package: <c>initialize</c>, the <c>initialized</c> notification, then
/// <c>tools/call</c>, each answer either plain JSON or one server-sent event. The
/// endpoint checks only that no browser Origin is sent (loopback guard); a test
/// client sends none.</para>
///
/// <para>A tool group answers only while its feature is on, and a feature switched
/// off answers as a tool that is not there — so a refusal names the feature to turn
/// on in Settings › Features.</para>
/// </summary>
internal sealed class HarnessMcp : IDisposable
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private string? _session;
    private int _nextId = 1;

    private HarnessMcp(HttpClient http, Uri endpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    public static async Task<HarnessMcp> ConnectAsync(AspireAppHost appHost, CancellationToken cancellationToken)
    {
        var baseUrl = await appHost.UrlAsync(DesktopHarness.Resource, cancellationToken);
        var mcp = new HarnessMcp(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, new Uri(baseUrl, "mcp"));

        await mcp.RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "backlog-e2e", ["version"] = "1" }
        }, cancellationToken);
        await mcp.NotifyAsync("notifications/initialized", cancellationToken);

        return mcp;
    }

    /// <summary>Calls one tool and answers its structured result. A tool that
    /// answered with an error throws with what it said.</summary>
    public async Task<JsonNode> CallAsync(string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        var result = await RequestAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments }, cancellationToken);

        if (result["isError"]?.GetValue<bool>() == true)
        {
            throw new InvalidOperationException($"{tool} refused: {Text(result)}");
        }

        return result["structuredContent"] ?? JsonNode.Parse(Text(result))
            ?? throw new InvalidOperationException($"{tool} answered nothing.");
    }

    private static string Text(JsonNode result) =>
        string.Join("\n", (result["content"]?.AsArray() ?? []).Select(item => item?["text"]?.GetValue<string>()).OfType<string>());

    private async Task<JsonNode> RequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        using var response = await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} answered {(int)response.StatusCode}: {body}");
        }

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessions)) _session ??= sessions.FirstOrDefault();

        var message = Messages(body, response.Content.Headers.ContentType?.MediaType)
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<int>() == id)
            ?? throw new InvalidOperationException($"{method} answered no message with id {id}: {body}");

        return message["error"] is { } error
            ? throw new InvalidOperationException($"{method} failed: {error.ToJsonString()}")
            : message["result"] ?? throw new InvalidOperationException($"{method} answered no result: {body}");
    }

    private async Task NotifyAsync(string method, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", ProtocolVersion);
        if (_session is not null) request.Headers.Add("Mcp-Session-Id", _session);

        return _http.SendAsync(request, cancellationToken);
    }

    /// <summary>The JSON-RPC messages in a response: the body itself, or each
    /// <c>data:</c> line of an event stream.</summary>
    private static IEnumerable<JsonNode> Messages(string body, string? mediaType)
    {
        if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            if (JsonNode.Parse(body) is { } single) yield return single;
            yield break;
        }

        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal)) continue;

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(trimmed[5..].Trim());
            }
            catch (JsonException)
            {
                continue;
            }

            if (parsed is not null) yield return parsed;
        }
    }

    public void Dispose() => _http.Dispose();
}
