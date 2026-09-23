using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Desktop.UI.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The hook telemetry a session forwards, against a fixture profile and fixture
/// transcripts.
/// <para>
/// Runs are started through <see cref="LocalDeliverySurfaceLifecycle"/> and read back
/// through <see cref="LocalDeliverySurfaceLifecycle.GetRunAsync"/> — the reader the pane
/// uses — for the reason <see cref="DeliverySurfaceLifecycleTests"/> gives: what
/// matters is what the row shows, not what the writer thinks it wrote.
/// </para>
/// </summary>
public sealed class DeliveryRunTelemetryTests : IDisposable
{
    private const string Session = "session-a";

    private static readonly string[] Stages = ["Scope Discovery", "Implementation", "Summary"];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-delivery-telemetry-tests",
        Guid.NewGuid().ToString("n"));

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero));

    private string Home => Path.Combine(_root, "home");

    private string Worktree => Path.Combine(_root, "worktree");

    private string Transcript => Path.Combine(_root, "transcripts", $"{Session}.jsonl");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DeliveryRunTelemetryTests()
    {
        Directory.CreateDirectory(Worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(Transcript)!);
        File.WriteAllText(Transcript, string.Empty);
    }

    [Fact]
    public async Task A_tool_call_is_recorded_on_the_run_with_its_duration_category_and_stage()
    {
        var runId = await StartRunAsync(stageInProgress: 1);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("PreToolUse", tool: "Bash"), Ct);
        _clock.Advance(TimeSpan.FromMilliseconds(1500));
        await telemetry.RecordAsync(Event("PostToolUse", tool: "Bash"), Ct);

        await telemetry.RecordAsync(Event("PreToolUse", tool: "mcp__plugin_qa_aspire__list_resources"), Ct);
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await telemetry.RecordAsync(Event("PostToolUse", tool: "mcp__plugin_qa_aspire__list_resources"), Ct);

        var run = await ReadAsync(runId);

        var shell = Assert.Single(run.InsightsByCategory, group => group.Name == "Shell");
        Assert.Equal(1, shell.Count);
        Assert.Equal(1500, shell.DurationMs);

        Assert.Contains(run.InsightsByCategory, group => group.Name == "QA (Playwright/Aspire)");
        Assert.Equal("plugin_qa_aspire", Assert.Single(run.InsightsByServer).Name);

        var recorded = (JsonObject)Document(runId)["insights"]![0]!;
        Assert.Equal(1, recorded["stageIndex"]!.GetValue<int>());
        Assert.Equal("Implementation", recorded["stageName"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_delegated_agent_is_recorded_beside_the_call_that_ran_it()
    {
        var runId = await StartRunAsync(stageInProgress: 0);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("PreToolUse", tool: "Agent"), Ct);
        await telemetry.RecordAsync(Event("PostToolUse", tool: "Agent", input: new JsonObject { ["subagent_type"] = "Explore", ["model"] = "sonnet" }), Ct);

        var agent = ((JsonArray)Document(runId)["insights"]!).OfType<JsonObject>().Single(entry => entry["kind"]!.GetValue<string>() == "agent");

        Assert.Equal("Explore", agent["agentName"]!.GetValue<string>());
        Assert.Equal("sonnet", agent["model"]!.GetValue<string>());
        Assert.Equal("completed", agent["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Tokens_are_folded_from_the_transcripts_once_per_message()
    {
        var runId = await StartRunAsync(stageInProgress: 1);
        var telemetry = Telemetry();

        // What was said before the run is not the run's: the first event only places
        // the cursor.
        Append(Transcript, Assistant("msg-before", input: 9_000, output: 9_000));
        await telemetry.RecordAsync(Event("Stop"), Ct);

        // One message written as two lines — one per content block, each repeating the
        // usage — then a second message; and a sub-agent's file beside the session.
        Append(Transcript, Assistant("msg-1", input: 100, output: 10, cacheRead: 1_000, cacheWrite: 50));
        Append(Transcript, Assistant("msg-1", input: 100, output: 10, cacheRead: 1_000, cacheWrite: 50));
        Append(Transcript, Assistant("msg-2", input: 200, output: 20, cacheRead: 2_000, cacheWrite: 0));

        var subAgent = Path.Combine(_root, "transcripts", Session, "subagents", "agent-1.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(subAgent)!);
        Append(subAgent, Assistant("msg-sub", input: 7, output: 3, model: "claude-sonnet-5"));

        await telemetry.RecordAsync(Event("Stop"), Ct);

        var run = await ReadAsync(runId);
        var usage = run.TokenUsage!;

        Assert.Equal(3, usage.Total.ModelCalls);
        Assert.Equal(307, usage.Total.InputTokens);
        Assert.Equal(33, usage.Total.OutputTokens);
        Assert.Equal(3_000, usage.Total.CacheReadTokens);
        Assert.Equal(50, usage.Total.CacheWriteTokens);

        Assert.Equal(1, usage.SubAgent.ModelCalls);
        Assert.Equal(7, usage.SubAgent.InputTokens);

        var stage = Assert.Single(usage.ByStage);
        Assert.Equal("Implementation", stage.StageName);
        Assert.Equal(3, stage.Total.ModelCalls);

        Assert.Equal(["claude-opus-5-5", "claude-sonnet-5"], usage.Models);

        // The gauge is the root's last prompt, cached or not; sub-agents run their own.
        Assert.Equal(2_200, run.Context!.CurrentTokens);
        Assert.Equal(1_000_000, run.Context.TokenLimit);
        Assert.Equal(2_200, run.Context.PeakTokens);

        // And nothing is counted twice when the next event arrives with nothing new.
        await telemetry.RecordAsync(Event("Stop"), Ct);
        Assert.Equal(3, (await ReadAsync(runId)).TokenUsage!.Total.ModelCalls);
    }

    [Fact]
    public async Task A_line_still_being_written_waits_for_the_next_event()
    {
        var runId = await StartRunAsync(stageInProgress: 0);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("Stop"), Ct);

        var line = Assistant("msg-1", input: 5, output: 5);
        File.AppendAllText(Transcript, line[..10]);
        await telemetry.RecordAsync(Event("Stop"), Ct);
        Assert.Null((await ReadAsync(runId)).TokenUsage);

        File.AppendAllText(Transcript, line[10..] + "\n");
        await telemetry.RecordAsync(Event("Stop"), Ct);
        Assert.Equal(1, (await ReadAsync(runId)).TokenUsage!.Total.ModelCalls);
    }

    [Fact]
    public async Task A_session_with_no_run_records_nothing_and_a_later_run_does_not_absorb_its_past()
    {
        var telemetry = Telemetry();

        Append(Transcript, Assistant("msg-before", input: 9_000, output: 9_000));
        await telemetry.RecordAsync(Event("PreToolUse", tool: "Bash"), Ct);
        await telemetry.RecordAsync(Event("PostToolUse", tool: "Bash"), Ct);

        Assert.False(Directory.Exists(Path.Combine(Home, "backlog")));

        var runId = await StartRunAsync(stageInProgress: 0);
        Append(Transcript, Assistant("msg-after", input: 1, output: 1));
        await telemetry.RecordAsync(Event("Stop"), Ct);

        Assert.Equal(1, (await ReadAsync(runId)).TokenUsage!.Total.ModelCalls);
    }

    [Fact]
    public async Task Another_sessions_run_is_left_alone()
    {
        var runId = await StartRunAsync(stageInProgress: 0, sessionId: "session-b");
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("PostToolUse", tool: "Bash"), Ct);

        Assert.Null(Document(runId)["insights"]);
        Assert.DoesNotContain(Session, (await ReadAsync(runId)).SessionIds);
    }

    [Fact]
    public async Task A_run_with_no_session_yet_is_adopted_by_the_first_one_that_reports()
    {
        var runId = await StartRunAsync(stageInProgress: 0, sessionId: null);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("PostToolUse", tool: "Edit"), Ct);

        var run = await ReadAsync(runId);
        Assert.Equal([Session], run.SessionIds);
        Assert.Equal("Edit", Assert.Single(run.InsightsByCategory).Name);
    }

    [Fact]
    public async Task A_session_in_a_subfolder_reports_to_the_worktree_above_it()
    {
        var runId = await StartRunAsync(stageInProgress: 0);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("PostToolUse", tool: "Bash", cwd: Path.Combine(Worktree, "src", "App")), Ct);

        Assert.Single((await ReadAsync(runId)).InsightsByCategory);
    }

    [Fact]
    public async Task A_finished_run_takes_no_more_telemetry()
    {
        var runId = await StartRunAsync(stageInProgress: 0);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("PostToolUse", tool: "Bash"), Ct);
        await Lifecycle().FinishRunAsync(Worktree, runId, "done", cancellationToken: Ct);
        await telemetry.RecordAsync(Event("PostToolUse", tool: "Bash"), Ct);

        Assert.Equal(1, Assert.Single((await ReadAsync(runId)).InsightsByCategory).Count);
    }

    [Fact]
    public async Task A_compaction_is_counted_on_the_run()
    {
        var runId = await StartRunAsync(stageInProgress: 0);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(Event("PreCompact", extra: new JsonObject { ["trigger"] = "auto" }), Ct);
        await telemetry.RecordAsync(Event("PreCompact", extra: new JsonObject { ["trigger"] = "manual" }), Ct);

        var compactions = (JsonArray)Document(runId)["context"]!["compactions"]!;

        Assert.Equal(["threshold", "manual"], compactions.Select(entry => entry!["reason"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Hook_telemetry_and_the_lifecycle_never_lose_each_others_writes()
    {
        var runId = await StartRunAsync(stageInProgress: 0);
        var telemetry = Telemetry();
        var lifecycle = Lifecycle();

        // Both read the whole document, change their part and write it back whole: the
        // collision a shared gate per run file exists to prevent.
        var hooks = Enumerable.Range(0, 20).Select(_ => telemetry.RecordAsync(Event("PostToolUse", tool: "Bash"), Ct));
        var prompts = Enumerable.Range(0, 20).Select(pass => lifecycle.RecordPromptAsync(Worktree, runId, $"Prompt {pass}", cancellationToken: Ct));

        await Task.WhenAll(hooks.Concat(prompts));

        var document = Document(runId);
        Assert.Equal(20, ((JsonArray)document["insights"]!).Count);
        Assert.Equal(20, ((JsonArray)document["promptHistory"]!).Count);
    }

    [Fact]
    public async Task Malformed_events_are_recorded_as_nothing()
    {
        var runId = await StartRunAsync(stageInProgress: 0);
        var telemetry = Telemetry();

        await telemetry.RecordAsync(JsonDocument.Parse("[]").RootElement, Ct);
        await telemetry.RecordAsync(JsonDocument.Parse("""{"hook_event_name": 7}""").RootElement, Ct);
        await telemetry.RecordAsync(Event("Stop", transcript: Path.Combine(_root, "missing.jsonl")), Ct);

        Assert.Null(Document(runId)["insights"]);
    }

    [Theory]
    [InlineData("Bash", "Shell")]
    [InlineData("PowerShell", "Shell")]
    [InlineData("Edit", "Edit")]
    [InlineData("Write", "Edit")]
    [InlineData("Read", "Read")]
    [InlineData("mcp__plugin_qa_playwright__browser_click", "QA (Playwright/Aspire)")]
    [InlineData("mcp__backlog__list_sessions", "MCP tool")]
    [InlineData("Agent", "Agent tasks")]
    [InlineData("Skill", "Agent tasks")]
    [InlineData("Artifact", "Other")]
    public void Tools_fall_into_the_dashboards_categories(string tool, string category) =>
        Assert.Equal(category, DeliveryRunTelemetry.CategoryOf(tool));

    [Theory]
    [InlineData("claude-haiku-4-5-20251001", 200_000)]
    [InlineData("claude-opus-5-5", 1_000_000)]
    [InlineData("claude-sonnet-5[1m]", 1_000_000)]
    [InlineData("some-other-model", 200_000)]
    public void The_gauge_is_a_share_of_the_models_window(string model, long limit) =>
        Assert.Equal(limit, DeliveryRunTelemetry.TokenLimitFor(model));

    [Fact]
    public async Task The_endpoint_takes_a_json_object_and_refuses_anything_else()
    {
        var sink = new RecordingTelemetry();

        Assert.Equal(204, await Accept("""{"hook_event_name":"Stop"}""", sink));
        Assert.Equal("Stop", Assert.Single(sink.Events));

        Assert.Equal(400, await Accept("[]", sink));
        Assert.Equal(400, await Accept("not json", sink));
        Assert.Equal(413, await Accept(new string(' ', BacklogTelemetryEndpoint.MaxBodyBytes + 1), sink));
        Assert.Equal(413, await BacklogTelemetryEndpoint.AcceptAsync(new MemoryStream(), BacklogTelemetryEndpoint.MaxBodyBytes + 1L, sink, Ct));

        Assert.Single(sink.Events);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Task<int> Accept(string body, IDeliveryRunTelemetry sink) =>
        BacklogTelemetryEndpoint.AcceptAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)), null, sink, Ct);

    private DeliveryRunTelemetry Telemetry() => new(Home, _clock);

    private LocalDeliverySurfaceLifecycle Lifecycle() => new(Home, "machine-id", "DEV-TOWER");

    private async Task<string> StartRunAsync(int stageInProgress, string? sessionId = Session)
    {
        var lifecycle = Lifecycle();
        var started = await lifecycle.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: sessionId, cancellationToken: Ct);

        await lifecycle.UpdateStageAsync(Worktree, started.RunId, stageInProgress, "in_progress", cancellationToken: Ct);

        return started.RunId;
    }

    private async Task<DeliveryRun> ReadAsync(string runId) =>
        (await Lifecycle().GetRunAsync(Worktree, runId, Ct))!;

    private JsonObject Document(string runId)
    {
        var path = Path.Combine(Home, "backlog", DeliveryRunWorktrees.KeyOf(Worktree)!, "runs", $"{runId}.json");

        return (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
    }

    private JsonElement Event(
        string name,
        string? tool = null,
        JsonObject? input = null,
        string? cwd = null,
        string? transcript = null,
        JsonObject? extra = null)
    {
        var payload = new JsonObject
        {
            ["hook_event_name"] = name,
            ["session_id"] = Session,
            ["cwd"] = cwd ?? Worktree,
            ["transcript_path"] = transcript ?? Transcript
        };

        if (tool is not null) payload["tool_name"] = tool;
        if (input is not null) payload["tool_input"] = input;

        foreach (var (key, value) in extra ?? [])
        {
            payload[key] = value?.DeepClone();
        }

        return JsonDocument.Parse(payload.ToJsonString()).RootElement;
    }

    private static string Assistant(
        string id,
        long input,
        long output,
        long cacheRead = 0,
        long cacheWrite = 0,
        string model = "claude-opus-5-5") =>
        new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = id,
                ["model"] = model,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = input,
                    ["output_tokens"] = output,
                    ["cache_read_input_tokens"] = cacheRead,
                    ["cache_creation_input_tokens"] = cacheWrite
                }
            }
        }.ToJsonString();

    private static void Append(string file, string line) => File.AppendAllText(file, line + "\n");

    private sealed class RecordingTelemetry : IDeliveryRunTelemetry
    {
        public List<string> Events { get; } = [];

        public Task RecordAsync(JsonElement hookEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(hookEvent.GetProperty("hook_event_name").GetString()!);

            return Task.CompletedTask;
        }
    }
}
