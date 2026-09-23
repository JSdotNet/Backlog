using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// The hook telemetry of a coding session, attributed to the delivery run it is driving —
/// the port of <c>delivery-surface-dashboard</c>'s <c>telemetry-hook.mjs</c> and
/// <c>insight.mjs</c>, writing the fields <see cref="DeliveryRunReader"/> already reads:
/// <c>insights</c>, <c>tokenUsage</c> and <c>context</c>.
/// <para>
/// <b>Which run.</b> The dashboard keeps an active-run pointer; this product has none,
/// so the run is found from the event itself. Its <c>cwd</c> names the worktree —
/// walked upwards, the way <see cref="DeliveryRunWorktrees.KeysUpFrom"/> is — and in
/// that worktree's folder the run in progress that lists the event's session wins,
/// else one in progress that lists no session yet, which the session is then added to.
/// A worktree whose runs in progress all belong to other sessions records nothing:
/// attributing one session's calls to another's run is the error this lookup exists
/// to avoid.
/// </para>
/// <para>
/// <b>Tokens come from the transcript, not the event.</b> A hook payload carries no
/// usage; the transcript it names does, one JSONL line per content block. Each file is
/// read from a cursor kept per session, so every line is counted once. Two
/// deliberate differences from the script this ports: a message split over several
/// lines repeats its <c>usage</c> on every one of them, and is counted once here rather
/// than once per line; and the cursors live in memory, so a restarted application
/// resumes at the end of the transcript — losing the gap rather than counting the whole
/// session again.
/// </para>
/// <para>
/// A session with no run to report to still moves its cursors to the end of its
/// transcript on every event, so that a run started later in the same session does not
/// absorb the whole conversation that came before it as its first stage.
/// </para>
/// </summary>
internal sealed partial class DeliveryRunTelemetry : IDeliveryRunTelemetry
{
    /// <summary>The dashboard's caps, so a very long run cannot grow its file without
    /// bound and the two writers' files stay the same size.</summary>
    internal const int MaxInsightsPerRun = 1000;

    internal const int MaxCompactionsPerRun = 100;

    private static readonly string[] TokenFields =
        ["inputTokens", "outputTokens", "reasoningTokens", "cacheReadTokens", "cacheWriteTokens"];

    private readonly DeliveryRunStore _store;
    private readonly TimeProvider _clock;

    /// <summary>One event at a time. Hooks for parallel tool calls arrive together, and
    /// the per-session bookkeeping below is plain dictionaries.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, SessionTrack> _sessions = new(StringComparer.Ordinal);

    /// <summary>What a host composes: the dashboards' folders under the signed-in
    /// profile, where <see cref="LocalDeliverySurfaceLifecycle"/> writes.</summary>
    internal DeliveryRunTelemetry()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"))
    {
    }

    internal DeliveryRunTelemetry(string home, TimeProvider? clock = null)
    {
        _store = new DeliveryRunStore(home);
        _clock = clock ?? TimeProvider.System;
    }

    public async Task RecordAsync(JsonElement hookEvent, CancellationToken cancellationToken = default)
    {
        if (hookEvent.ValueKind is not JsonValueKind.Object) return;

        if (Text(hookEvent, "hook_event_name") is not { } name) return;

        var sessionId = Text(hookEvent, "session_id") ?? "default";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await RecordAsync(name, sessionId, hookEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Best effort by contract: a transcript mid-rewrite or a run file somebody
            // hand-edited costs this one event, never the hook that reported it.
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RecordAsync(string name, string sessionId, JsonElement hookEvent, CancellationToken cancellationToken)
    {
        var track = TrackFor(sessionId);
        var now = _clock.GetUtcNow();
        var transcript = Text(hookEvent, "transcript_path");

        // A start time and nothing else: it touches no run, so it stays cheap for the
        // sessions that have none.
        if (name == "PreToolUse")
        {
            track.Started(Text(hookEvent, "tool_name") ?? "unknown", now);
            return;
        }

        var found = await FindRunAsync(track, Text(hookEvent, "cwd"), sessionId, cancellationToken).ConfigureAwait(false);

        if (found is { } run)
        {
            await RecordOnAsync(run.Worktree, run.RunId, name, sessionId, track, hookEvent, transcript, now, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            SkipToEnd(track, transcript);
        }

        if (name == "SessionEnd") _sessions.Remove(sessionId);
    }

    private async Task RecordOnAsync(
        string worktree,
        string runId,
        string name,
        string sessionId,
        SessionTrack track,
        JsonElement hookEvent,
        string? transcript,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var held = await _store.LockAsync(worktree, runId, cancellationToken).ConfigureAwait(false);

        // Re-read under the gate: the lifecycle may have written between the lookup and
        // now, and this document is written back whole.
        var run = await _store.ReadAsync(worktree, runId, cancellationToken).ConfigureAwait(false);

        // Finished since, or claimed by another session since this one adopted it.
        if (run is null || !InProgress(run) || !(HasSession(run, sessionId) || run["sessionIds"] is not JsonArray { Count: > 0 }))
        {
            track.Run = null;
            SkipToEnd(track, transcript);
            return;
        }

        var stage = StageInProgress(run);

        switch (name)
        {
            case "PostToolUse":
            {
                var tool = Text(hookEvent, "tool_name") ?? "unknown";
                var durationMs = track.Finished(tool) is { } startedAt
                    ? Math.Max(0L, (long)(now - startedAt).TotalMilliseconds)
                    : 0L;

                AppendInsight(run, ToolCall(tool, durationMs, now, track.LastModel, stage));

                // A Task or Agent call is the one place a hook learns which sub-agent
                // ran, so it doubles as the dashboard's agent record.
                if (tool is "Task" or "Agent")
                {
                    AppendInsight(run, AgentUse(hookEvent, durationMs, now, track.LastModel, stage));
                }

                Sync(run, track, transcript, stage, now);
                break;
            }

            case "PreCompact":
                RecordCompaction(run, Text(hookEvent, "trigger") == "manual" ? "manual" : "threshold", now);
                break;

            case "Stop" or "SubagentStop" or "SessionEnd":
                Sync(run, track, transcript, stage, now);
                break;

            default:
                return;
        }

        WithSession(run, sessionId);

        await _store.WriteAsync(worktree, runId, run, cancellationToken).ConfigureAwait(false);
    }

    private SessionTrack TrackFor(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var track))
        {
            track = new SessionTrack();
            _sessions[sessionId] = track;
        }

        return track;
    }

    // ── Which run ──────────────────────────────────────────────────────────────

    /// <summary>The run this session's events belong to, or null. The last answer is
    /// reused without a read, because the lookup reads every run file in a worktree and
    /// this runs on every tool call; <see cref="RecordOnAsync"/> checks it still holds,
    /// under the run's gate, where no write of this process can be halfway through.</summary>
    private async Task<(string Worktree, string RunId)?> FindRunAsync(
        SessionTrack track,
        string? cwd,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (track.Run is { } known) return known;

        foreach (var folder in FoldersUpFrom(cwd))
        {
            var live = (await _store.ReadAllAsync(folder, cancellationToken).ConfigureAwait(false))
                .Where(run => Text(run, "id") is { Length: > 0 } && InProgress(run))
                .OrderByDescending(run => Text(run, "updatedAt") ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            if (live.Count == 0) continue;

            var mine = live.FirstOrDefault(run => HasSession(run, sessionId))
                ?? live.FirstOrDefault(run => run["sessionIds"] is not JsonArray { Count: > 0 });

            // Runs in progress here, every one of them another session's: this
            // worktree is spoken for, and a folder further up is not a better answer.
            if (mine is null) return null;

            track.Run = (folder, Text(mine, "id")!);

            return track.Run;
        }

        return null;
    }

    /// <summary>A folder and every folder above it, nearest first — the paths
    /// <see cref="DeliveryRunWorktrees.KeysUpFrom"/> derives its keys from.</summary>
    private static IEnumerable<string> FoldersUpFrom(string? folder)
    {
        var current = folder?.TrimEnd('\\', '/');

        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;

            var cut = Math.Max(current.LastIndexOf('\\'), current.LastIndexOf('/'));

            if (cut <= 0) yield break;

            current = current[..cut].TrimEnd('\\', '/');
        }
    }

    private static bool InProgress(JsonObject run) =>
        string.Equals(Text(run, "status"), DeliveryRunStatuses.InProgress, StringComparison.OrdinalIgnoreCase);

    private static bool HasSession(JsonObject run, string sessionId) =>
        run["sessionIds"] is JsonArray ids
        && ids.Any(id => id is JsonValue value && value.TryGetValue<string>(out var text) && string.Equals(text, sessionId, StringComparison.Ordinal));

    private static void WithSession(JsonObject run, string sessionId)
    {
        if (HasSession(run, sessionId)) return;

        if (run["sessionIds"] is not JsonArray ids)
        {
            run["sessionIds"] = ids = [];
        }

        ids.Add(sessionId);
    }

    /// <summary>The stage the event happened in: the last one the flow marked
    /// <c>in_progress</c>, or null between stages.</summary>
    private static StageRef? StageInProgress(JsonObject run)
    {
        if (run["stages"] is not JsonArray stages) return null;

        for (var index = stages.Count - 1; index >= 0; index--)
        {
            if (stages[index] is JsonObject stage
                && string.Equals(Text(stage, "status"), DeliveryStageStatuses.InProgress, StringComparison.Ordinal))
            {
                return new StageRef(index, Text(stage, "name") ?? string.Empty);
            }
        }

        return null;
    }

    // ── Tool calls ─────────────────────────────────────────────────────────────

    private static JsonObject ToolCall(string tool, long durationMs, DateTimeOffset now, string? model, StageRef? stage)
    {
        var entry = new JsonObject
        {
            ["kind"] = "tool",
            ["toolName"] = tool,
            ["category"] = CategoryOf(tool),
            ["durationMs"] = durationMs,

            // PostToolUse fires for a call that completed; a failed one is a separate
            // event this plugin does not forward.
            ["success"] = true,
            ["endedAt"] = Stamp(now)
        };

        if (McpServerOf(tool) is { } server) entry["mcpServerName"] = server;
        if (model is not null) entry["model"] = model;

        return WithStage(entry, stage);
    }

    private static JsonObject AgentUse(JsonElement hookEvent, long durationMs, DateTimeOffset now, string? model, StageRef? stage)
    {
        var input = hookEvent.TryGetProperty("tool_input", out var given) && given.ValueKind is JsonValueKind.Object
            ? given
            : default;

        var agent = Text(input, "subagent_type");

        return WithStage(new JsonObject
        {
            ["kind"] = "agent",
            ["agentName"] = agent ?? "subagent",
            ["agentDisplayName"] = agent ?? Text(input, "description") ?? "subagent",
            ["model"] = Text(input, "model") ?? model,
            ["status"] = "completed",
            ["durationMs"] = durationMs,
            ["endedAt"] = Stamp(now)
        }, stage);
    }

    private static JsonObject WithStage(JsonObject entry, StageRef? stage)
    {
        entry["stageIndex"] = stage?.Index;
        entry["stageName"] = stage?.Name;

        return entry;
    }

    private static void AppendInsight(JsonObject run, JsonObject entry)
    {
        if (run["insights"] is not JsonArray insights)
        {
            run["insights"] = insights = [];
        }

        insights.Add(entry);

        while (insights.Count > MaxInsightsPerRun) insights.RemoveAt(0);
    }

    /// <summary>
    /// The dashboard's categories, rule for rule and in its order: QA is tested before
    /// the generic MCP rule so browser and Aspire activity gets a bucket of its own.
    /// Both hosts' spellings are in the rules, which is why <c>view</c> and
    /// <c>create</c> are there.
    /// </summary>
    internal static string CategoryOf(string tool)
    {
        if (ShellRule().IsMatch(tool)) return "Shell";
        if (EditRule().IsMatch(tool)) return "Edit";
        if (ReadRule().IsMatch(tool)) return "Read";
        if (QaRule().IsMatch(tool)) return "QA (Playwright/Aspire)";
        if (McpRule().IsMatch(tool)) return "MCP tool";
        if (AgentRule().IsMatch(tool)) return "Agent tasks";

        return "Other";
    }

    /// <summary>The server an MCP tool belongs to — <c>mcp__&lt;server&gt;__&lt;tool&gt;</c>
    /// — or null for a built-in tool.</summary>
    internal static string? McpServerOf(string tool) =>
        McpServerName().Match(tool) is { Success: true } match ? match.Groups[1].Value : null;

    // ── Tokens and the context gauge ───────────────────────────────────────────

    /// <summary>Folds everything written to the session's transcripts since the last
    /// event: the root file, then each sub-agent's file beside it.</summary>
    private void Sync(JsonObject run, SessionTrack track, string? transcript, StageRef? stage, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;

        // No cursor for this transcript means the session has not been seen reading it
        // — an application restarted mid-run, or a session moved to a new file. Start
        // at its end: the gap is lost, which is better than a whole session counted as
        // this run's.
        if (!string.Equals(track.TranscriptPath, transcript, StringComparison.OrdinalIgnoreCase))
        {
            SkipToEnd(track, transcript);
            return;
        }

        var root = Fold(run, transcript, track.Root, subAgent: false, stage, now);

        foreach (var file in SubAgentTranscripts(transcript))
        {
            if (!track.SubAgents.TryGetValue(file, out var cursor))
            {
                cursor = new Cursor();
                track.SubAgents[file] = cursor;
            }

            Fold(run, file, cursor, subAgent: true, stage, now);
        }

        if (root.LastModel is not null) track.LastModel = root.LastModel;

        // Sub-agents run a context window of their own, so only the root's samples
        // drive the gauge.
        if (root.LastSample is { } sample) RecordContextSample(run, sample.CurrentTokens, sample.TokenLimit, now);
    }

    /// <summary>Moves every cursor to the end of what is written now.</summary>
    private static void SkipToEnd(SessionTrack track, string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;

        track.TranscriptPath = transcript;
        track.Root = new Cursor { Offset = LengthOf(transcript) };
        track.SubAgents.Clear();

        foreach (var file in SubAgentTranscripts(transcript))
        {
            track.SubAgents[file] = new Cursor { Offset = LengthOf(file) };
        }
    }

    /// <summary>
    /// A session's sub-agent transcripts. Claude Code writes a delegated agent's
    /// messages to a file of its own beside the session's —
    /// <c>&lt;session&gt;/subagents/agent-*.jsonl</c> — so reading only the file the
    /// event names leaves every delegated token uncounted.
    /// </summary>
    private static IEnumerable<string> SubAgentTranscripts(string transcript)
    {
        var folder = Path.Combine(
            transcript.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ? transcript[..^".jsonl".Length] : transcript,
            "subagents");

        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jsonl") : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long LengthOf(string file)
    {
        try
        {
            return File.Exists(file) ? new FileInfo(file).Length : 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Reads one transcript from its cursor to the last whole line, and folds
    /// every assistant message's usage into the run.</summary>
    private static Folded Fold(JsonObject run, string file, Cursor cursor, bool subAgent, StageRef? stage, DateTimeOffset now)
    {
        var size = LengthOf(file);

        // A transcript shorter than the cursor was rewritten: start it over.
        if (cursor.Offset > size)
        {
            cursor.Offset = 0;
            cursor.LastMessageId = null;
        }

        if (cursor.Offset == size) return default;

        byte[] bytes;

        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Seek(cursor.Offset, SeekOrigin.Begin);
            bytes = new byte[size - cursor.Offset];
            stream.ReadExactly(bytes);
        }

        // Whole lines only; a line still being written is left for the next event.
        var end = Array.LastIndexOf(bytes, (byte)'\n');

        if (end < 0) return default;

        string? lastModel = null;
        Sample? lastSample = null;

        foreach (var line in Encoding.UTF8.GetString(bytes, 0, end + 1).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var entry = document.RootElement;

                if (entry.ValueKind is not JsonValueKind.Object || Text(entry, "type") != "assistant") continue;
                if (!entry.TryGetProperty("message", out var message) || message.ValueKind is not JsonValueKind.Object) continue;
                if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind is not JsonValueKind.Object) continue;

                // One message, several lines — one per content block, each repeating
                // the message's usage. They are written consecutively, so the last id
                // counted is enough to count each message once.
                var id = Text(message, "id");

                if (id is not null && string.Equals(id, cursor.LastMessageId, StringComparison.Ordinal)) continue;

                cursor.LastMessageId = id;

                var model = Text(message, "model");
                var sidechain = subAgent || (entry.TryGetProperty("isSidechain", out var flag) && flag.ValueKind is JsonValueKind.True);

                var input = Number(usage, "input_tokens");
                var cacheRead = Number(usage, "cache_read_input_tokens");
                var cacheWrite = Number(usage, "cache_creation_input_tokens");

                RecordTokenUsage(run, input, Number(usage, "output_tokens"), cacheRead, cacheWrite, model, sidechain, stage, now);

                if (!sidechain)
                {
                    if (model is not null) lastModel = model;

                    // Everything the context window held for that call, cached or not —
                    // the closest a transcript comes to the session's own gauge.
                    lastSample = new Sample(input + cacheRead + cacheWrite, TokenLimitFor(model));
                }
            }
        }

        // Counted in bytes, which is what the offset is: a character count would drift
        // on the first line that held anything outside ASCII.
        cursor.Offset += end + 1;

        return new Folded(lastModel, lastSample);
    }

    /// <summary>
    /// One model call into the run's buckets: the total, the sub-agent subtotal when it
    /// was delegated work, and the same pair for the stage in progress. Delegated work
    /// is in the total too — it is still what the stage cost — and in its own subtotal
    /// so that it stays visible where the cost came from.
    /// </summary>
    private static void RecordTokenUsage(
        JsonObject run,
        long input,
        long output,
        long cacheRead,
        long cacheWrite,
        string? model,
        bool subAgent,
        StageRef? stage,
        DateTimeOffset now)
    {
        var usage = Child(run, "tokenUsage");

        AddTo(Child(usage, "total"), input, output, cacheRead, cacheWrite);
        if (subAgent) AddTo(Child(usage, "subAgent"), input, output, cacheRead, cacheWrite);

        if (model is not null)
        {
            if (usage["models"] is not JsonArray models)
            {
                usage["models"] = models = [];
            }

            if (!models.Any(known => known is JsonValue value && value.TryGetValue<string>(out var seen) && seen == model))
            {
                models.Add(model);
            }
        }

        if (stage is { } current)
        {
            var byStage = Child(usage, "byStage");
            var key = current.Index.ToString(CultureInfo.InvariantCulture);
            var entry = Child(byStage, key);

            if (!string.IsNullOrEmpty(current.Name) || entry["stageName"] is null) entry["stageName"] = current.Name;

            AddTo(Child(entry, "total"), input, output, cacheRead, cacheWrite);
            if (subAgent) AddTo(Child(entry, "subAgent"), input, output, cacheRead, cacheWrite);
        }

        usage["updatedAt"] = Stamp(now);
    }

    private static void AddTo(JsonObject bucket, long input, long output, long cacheRead, long cacheWrite)
    {
        Increase(bucket, "modelCalls", 1);
        Increase(bucket, "inputTokens", input);
        Increase(bucket, "outputTokens", output);
        Increase(bucket, "cacheReadTokens", cacheRead);
        Increase(bucket, "cacheWriteTokens", cacheWrite);

        // Claude's transcript reports no reasoning split; the field is kept present so
        // a bucket written here has the dashboard's whole shape.
        foreach (var field in TokenFields) bucket[field] ??= 0L;
    }

    private static void Increase(JsonObject owner, string name, long by) =>
        owner[name] = Math.Max(0L, Number(owner, name)) + Math.Max(0L, by);

    private static void RecordContextSample(JsonObject run, long currentTokens, long tokenLimit, DateTimeOffset now)
    {
        var context = Child(run, "context");

        context["currentTokens"] = currentTokens;
        context["tokenLimit"] = tokenLimit;

        // A prompt the model actually read is proof of a window at least that large;
        // trusting the stated limit over it would pin the gauge above 100% for the run.
        if (currentTokens > tokenLimit)
        {
            context["tokenLimit"] = currentTokens;
            context["tokenLimitInferred"] = true;
        }

        context["peakTokens"] = Math.Max(Number(context, "peakTokens"), currentTokens);
        context["sampledAt"] = Stamp(now);
    }

    private static void RecordCompaction(JsonObject run, string reason, DateTimeOffset now)
    {
        var context = Child(run, "context");

        if (context["compactions"] is not JsonArray compactions)
        {
            context["compactions"] = compactions = [];
        }

        compactions.Add(new JsonObject
        {
            ["reason"] = reason,
            ["currentTokens"] = context["currentTokens"] is JsonValue ? Number(context, "currentTokens") : null,
            ["at"] = Stamp(now)
        });

        while (compactions.Count > MaxCompactionsPerRun) compactions.RemoveAt(0);
    }

    /// <summary>
    /// The window a model's gauge is a percentage of. Current Claude models are
    /// 1M-context and Haiku is the 200k exception; an unrecognised model is assumed
    /// small, so a gauge reads full early rather than late.
    /// </summary>
    internal static long TokenLimitFor(string? model)
    {
        var id = model ?? string.Empty;

        if (OneMillionSuffix().IsMatch(id)) return 1_000_000;
        if (id.Contains("haiku", StringComparison.OrdinalIgnoreCase)) return 200_000;
        if (LargeWindowFamily().IsMatch(id)) return 1_000_000;

        return 200_000;
    }

    // ── Plumbing ───────────────────────────────────────────────────────────────

    private static JsonObject Child(JsonObject owner, string name)
    {
        if (owner[name] is JsonObject child) return child;

        child = [];
        owner[name] = child;

        return child;
    }

    private static string Stamp(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? Text(JsonObject owner, string name) =>
        owner[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? Text(JsonElement owner, string name) =>
        owner.ValueKind is JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Number(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out var number)
            ? Math.Max(0L, number)
            : 0L;

    private static long Number(JsonObject owner, string name) =>
        owner[name] is JsonValue value
            ? value.TryGetValue<long>(out var whole) ? whole
            : value.TryGetValue<double>(out var fraction) ? (long)Math.Round(fraction)
            : 0L
            : 0L;

    [GeneratedRegex("powershell|bash|shell", RegexOptions.IgnoreCase)]
    private static partial Regex ShellRule();

    [GeneratedRegex("^edit$|^create$|^write$|^notebookedit$|^multiedit$", RegexOptions.IgnoreCase)]
    private static partial Regex EditRule();

    [GeneratedRegex("^view$|^read$|^glob$|^grep$|^webfetch$|^websearch$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadRule();

    /// <summary>Browser and Aspire tools, including the Aspire server's current
    /// <c>list_*</c> names and its legacy <c>get_*</c> ones, which a run may hold
    /// either of.</summary>
    [GeneratedRegex(
        "^browser_|playwright|aspire|^list_resources$|^list_structured_logs$|^list_console_logs$|^list_traces$|^list_trace_structured_logs$|^execute_resource_command$|^get_resources$|^get_resource_logs$|^get_traces$|^get_metrics$|^get_console_logs$",
        RegexOptions.IgnoreCase)]
    private static partial Regex QaRule();

    [GeneratedRegex("mcpserver|mcp[_-]", RegexOptions.IgnoreCase)]
    private static partial Regex McpRule();

    [GeneratedRegex("^task$|^agent$|^skill$|^workflow$", RegexOptions.IgnoreCase)]
    private static partial Regex AgentRule();

    [GeneratedRegex("^mcp__([^_]+(?:_[^_]+)*?)__")]
    private static partial Regex McpServerName();

    [GeneratedRegex(@"\[1m\]|-1m\b", RegexOptions.IgnoreCase)]
    private static partial Regex OneMillionSuffix();

    [GeneratedRegex("opus|sonnet|fable|mythos", RegexOptions.IgnoreCase)]
    private static partial Regex LargeWindowFamily();

    private readonly record struct StageRef(int Index, string Name);

    private readonly record struct Sample(long CurrentTokens, long TokenLimit);

    private readonly record struct Folded(string? LastModel, Sample? LastSample);

    private sealed class Cursor
    {
        public long Offset { get; set; }

        public string? LastMessageId { get; set; }
    }

    /// <summary>What one session's events have left to remember between them.</summary>
    private sealed class SessionTrack
    {
        private readonly Dictionary<string, Queue<DateTimeOffset>> _pending = new(StringComparer.Ordinal);

        public (string Worktree, string RunId)? Run { get; set; }

        public string? TranscriptPath { get; set; }

        public Cursor Root { get; set; } = new();

        public Dictionary<string, Cursor> SubAgents { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? LastModel { get; set; }

        /// <summary>Starts are queued per tool name and matched first-in first-out,
        /// which is the order parallel calls of one tool complete in often enough for a
        /// duration and never worse than no duration at all.</summary>
        public void Started(string tool, DateTimeOffset at)
        {
            if (!_pending.TryGetValue(tool, out var starts))
            {
                starts = new Queue<DateTimeOffset>();
                _pending[tool] = starts;
            }

            starts.Enqueue(at);
        }

        public DateTimeOffset? Finished(string tool) =>
            _pending.TryGetValue(tool, out var starts) && starts.TryDequeue(out var at) ? at : null;
    }
}
