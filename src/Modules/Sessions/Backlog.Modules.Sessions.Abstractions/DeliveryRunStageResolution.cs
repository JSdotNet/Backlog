using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>How a stage of a delivery run ran.</summary>
public enum DeliveryRunStageMode
{
    /// <summary>In the owner session.</summary>
    Inline,

    /// <summary>Handed to a sub-agent.</summary>
    Delegate,

    /// <summary>In a fork of the owner session.</summary>
    Fork,

    /// <summary>The person's approval: no model runs it.</summary>
    Gate
}

/// <summary>A field a stage can run differently from its configuration on.</summary>
public enum DeliveryRunStageField
{
    Agent,
    Model,
    Effort
}

/// <summary>
/// One field a stage ran differently from what its run resolved for it.
/// </summary>
/// <param name="Field">Which field.</param>
/// <param name="Configured">What the run resolved, verbatim; null for the session's.</param>
/// <param name="Ran">What ran, verbatim; null for the session's.</param>
public sealed record DeliveryRunStageDifference(DeliveryRunStageField Field, string? Configured, string? Ran);

/// <summary>
/// What a run resolved for a stage before it ran — its <c>runContext.phases</c> entry —
/// and where that came from.
/// </summary>
/// <param name="Agent">The agent the phase follows; null inline.</param>
/// <param name="Model">The model alias or id; null for the session's.</param>
/// <param name="Effort">The effort; null for the session's.</param>
/// <param name="Origin">Where the entry came from — the team default, or an overlay
/// over it — as the run recorded it, or null where it recorded nothing to say so.</param>
public sealed record DeliveryRunStageConfiguration(string? Agent, string? Model, string? Effort, string? Origin);

/// <summary>
/// One sub-agent call a stage made.
/// </summary>
/// <param name="Agent">The agent, as recorded.</param>
/// <param name="Model">The model it ran on, verbatim, where recorded.</param>
/// <param name="Effort">The effort it ran at, where the stage's execution recorded it.</param>
/// <param name="Slice">The slice it worked, where the stage's execution named one.</param>
/// <param name="DurationMs">How long it ran, where recorded.</param>
/// <param name="Tokens">The tokens it moved, where recorded.</param>
/// <param name="ToolCalls">The tool calls it made, where recorded.</param>
/// <param name="Failed">Whether it did not complete.</param>
/// <param name="Declared">Whether only the stage's declaration names it: nothing
/// shows it running.</param>
/// <param name="Revise">Whether it ran under the gate — a revise round's work.</param>
/// <param name="Observed">Whether the run's insights saw the call, so its time and
/// outcome are recorded; a call only the stage's execution lists has neither.</param>
public sealed record DeliveryRunStageWorker(
    string Agent,
    string? Model,
    string? Effort,
    string? Slice,
    long? DurationMs,
    long? Tokens,
    int? ToolCalls,
    bool Failed,
    bool Declared,
    bool Revise,
    bool Observed)
{
    /// <summary>When the call returned, where the run's insights recorded it — what,
    /// with <see cref="DurationMs"/>, places the call's own requests in time.</summary>
    public DateTimeOffset? EndedAt { get; init; }
}

/// <summary>
/// How one stage of a delivery run ran, read off the run: its recorded execution, the
/// sub-agents seen in it, and the phase map the run resolved — see
/// <see cref="DeliveryRunStageResolution"/>.
/// </summary>
/// <param name="Name">The stage's name.</param>
/// <param name="Status">Its status word, verbatim.</param>
/// <param name="Mode">How it ran.</param>
/// <param name="ModeRecorded">Whether the run recorded the mode, rather than it being
/// inferred from what was seen. The gate is always known.</param>
/// <param name="Agent">The agent whose instructions it ran; null in the owner session.</param>
/// <param name="Model">The model it ran on, verbatim, where known.</param>
/// <param name="Effort">The effort it ran at, where known.</param>
/// <param name="Skill">The skill it followed, where recorded.</param>
/// <param name="Mcp">The MCP servers it was given: empty for none, null where nothing
/// was recorded.</param>
/// <param name="Before">The skills run before it.</param>
/// <param name="After">The skills run after it.</param>
/// <param name="Fallback">The configured id that did not resolve, where one did not.</param>
/// <param name="Configured">What the run resolved for it, or null where the run records
/// no resolved phase for it.</param>
/// <param name="Differences">Where it ran differently from <paramref name="Configured"/>.</param>
/// <param name="Workers">The sub-agent calls it made, in the order recorded.</param>
/// <param name="Passes">How many times it was marked done.</param>
/// <param name="DurationMs">Its wall time, where stamped.</param>
/// <param name="OutputTokens">The output tokens it spent, where recorded.</param>
/// <param name="ToolCalls">The tool calls filed under it.</param>
/// <param name="RunRecordsPhases">Whether the run recorded a resolved phase map at
/// all — what tells a stage the map has no entry for from a run that kept none.</param>
public sealed record DeliveryRunStageExecution(
    string Name,
    string Status,
    DeliveryRunStageMode Mode,
    bool ModeRecorded,
    string? Agent,
    string? Model,
    string? Effort,
    string? Skill,
    IReadOnlyList<string>? Mcp,
    IReadOnlyList<string> Before,
    IReadOnlyList<string> After,
    string? Fallback,
    DeliveryRunStageConfiguration? Configured,
    IReadOnlyList<DeliveryRunStageDifference> Differences,
    IReadOnlyList<DeliveryRunStageWorker> Workers,
    int Passes,
    long? DurationMs,
    long? OutputTokens,
    int ToolCalls,
    bool RunRecordsPhases)
{
    /// <summary>Whether the stage ran other than as configured — the ≠ mark.</summary>
    public bool Drifted => Differences.Count > 0 || Fallback is not null;

    /// <summary>Whether a value on the stage was inferred rather than recorded — the ?
    /// mark. The mode is the value inference decides.</summary>
    public bool Inferred => !ModeRecorded;
}

/// <summary>
/// Reads how each stage of a delivery run ran — the port of the stage resolution the
/// <c>delivery-run-view</c> plugin draws its pane with, so this product and that pane
/// say the same thing about the same run.
/// <para>
/// Three sources, most specific first: the stage's own <c>execution</c>, what actually
/// ran; the sub-agents the run's insights saw in the stage; and the run context's
/// resolved phase map, what the configuration asked for. Where the run records no
/// resolved phases, the mode and the agent are inferred from what was seen.
/// </para>
/// <list type="bullet">
/// <item><b>Mode.</b> Personal Validation is always the gate; a recorded mode wins; a
/// stage a sub-agent worked in was delegated; the rest ran inline.</item>
/// <item><b>Worker.</b> The bound agent, or the agent behind a
/// <c>delivery:runner-&lt;effort&gt;</c> runner — else the longest-running, so a log
/// monitor beside the agent does not name the phase.</item>
/// <item><b>Model and effort.</b> What ran, then the runner, then the
/// configuration.</item>
/// <item><b>Configured against ran.</b> Compared on agent, model family and effort,
/// only for a stage past pending and never for the gate.</item>
/// </list>
/// </summary>
public static partial class DeliveryRunStageResolution
{
    /// <summary>Every stage of the run, resolved, in run order.</summary>
    public static IReadOnlyList<DeliveryRunStageExecution> Of(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var context = Parse(run.RunContext);
        var map = PhaseMap(context, run.SkillId);
        var origin = LayerOrigin(context);

        return [.. run.Stages.Select((stage, index) => Resolve(run, stage, index, map, origin))];
    }

    /// <summary>Whether the run recorded a resolved phase map at all.</summary>
    public static bool RecordsPhases(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return PhaseMap(Parse(run.RunContext), run.SkillId) is { Count: > 0 };
    }

    /// <summary>
    /// What the stage's marks stand for, in words: the configured id that did not
    /// resolve, each field that ran other than configured, and an inference. The
    /// panel states these; the ≠ and ? marks summarise them.
    /// </summary>
    public static IReadOnlyList<string> Explanations(DeliveryRunStageExecution stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        var lines = new List<string>();

        if (stage.Fallback is { } fallback)
        {
            lines.Add($"{fallback} did not resolve; ran {stage.Agent ?? "in the owner session"}.");
        }

        if (stage.Differences.Count > 0)
        {
            lines.Add($"Configured vs ran: {DifferenceText(stage.Differences)}.");
        }

        if (stage.Inferred)
        {
            lines.Add(!stage.RunRecordsPhases
                ? "This run records no resolved phases: mode and agent are inferred."
                : stage.Configured is null
                    ? "The run's phase map has no entry for this stage: its mode is inferred."
                    : "Neither the stage nor its phase entry recorded a mode: it is inferred.");
        }

        return lines;
    }

    /// <summary>Each difference as "field configured → ran", joined by " · " — the ≠
    /// mark's title after "Not as configured: ".</summary>
    public static string DifferenceText(IEnumerable<DeliveryRunStageDifference> differences)
    {
        ArgumentNullException.ThrowIfNull(differences);

        return string.Join(" · ", differences.Select(difference =>
            $"{FieldWord(difference.Field)} {Shown(difference.Field, difference.Configured)} → {Shown(difference.Field, difference.Ran)}"));
    }

    /// <summary>The ≠ mark's title: every difference, and the fallback first.</summary>
    public static string DriftTitle(DeliveryRunStageExecution stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        var parts = new List<string>();

        if (stage.Fallback is { } fallback) parts.Add($"{fallback} did not resolve");
        if (stage.Differences.Count > 0) parts.Add(DifferenceText(stage.Differences));

        return $"Not as configured: {string.Join(" · ", parts)}";
    }

    /// <summary>
    /// The family a model belongs to, so the alias a configuration names (<c>opus</c>)
    /// and the id telemetry records (<c>claude-opus-5-5</c>) compare equal.
    /// </summary>
    public static string Family(string model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var label = AgentModels.Label(model);
        var cut = label.IndexOfAny([' ', '[']);

        return (cut > 0 ? label[..cut] : label).ToLowerInvariant();
    }

    private static string FieldWord(DeliveryRunStageField field) => field switch
    {
        DeliveryRunStageField.Agent => "agent",
        DeliveryRunStageField.Model => "model",
        _ => "effort"
    };

    private static string Shown(DeliveryRunStageField field, string? value) =>
        value is null ? "session" : field is DeliveryRunStageField.Model ? AgentModels.Label(value) : value;

    private static DeliveryRunStageExecution Resolve(
        DeliveryRun run,
        DeliveryRunStage stage,
        int index,
        JsonObject? map,
        string? layerOrigin)
    {
        var ran = Parse(stage.Execution) ?? new JsonObject();
        var resolved = Entry(map, stage.Name, Text(ran, "qualifier"));

        // The resolved entry under what ran: a field the stage recorded wins, even
        // when it recorded null.
        var execution = new JsonObject();
        foreach (var (key, value) in resolved ?? new JsonObject()) execution[key] = value?.DeepClone();
        foreach (var (key, value) in ran) execution[key] = value?.DeepClone();

        var isGate = GateName().IsMatch(stage.Name) || ModeOf(Text(execution, "mode")) is DeliveryRunStageMode.Gate;
        var workers = Workers(stage, ran, isGate);

        // A legacy writer put the mode in "runs" as a word; every later one puts the
        // calls there as a list.
        var recorded = ModeOf(execution["runs"] is JsonValue legacy && legacy.TryGetValue<string>(out var word) ? word : Text(execution, "mode"));

        var mode = isGate
            ? DeliveryRunStageMode.Gate
            : recorded is { } known and not DeliveryRunStageMode.Gate
                ? known
                : workers.Count > 0
                    ? DeliveryRunStageMode.Delegate
                    : DeliveryRunStageMode.Inline;

        // What ran comes from the stage's own record, then from the sub-agents seen,
        // and only with neither from what was resolved — so a configured agent that
        // never ran is not shown as if it had. A stage that recorded running inline
        // with no agent ran in the owner session, whatever was configured.
        var ranAgent = Text(ran, "agent");
        var recordedNoAgent = ran.ContainsKey("agent") && ranAgent is null && mode is DeliveryRunStageMode.Inline;
        var worker = mode is DeliveryRunStageMode.Delegate or DeliveryRunStageMode.Fork
            ? BoundWorker(workers, ranAgent ?? Text(resolved, "agent"))
            : null;
        var runnerEffort = worker is null ? null : RunnerEffort(worker.Agent);
        var workerAgent = runnerEffort is not null ? Text(resolved, "agent") ?? "general-purpose" : worker?.Agent;
        var agent = ranAgent ?? workerAgent ?? (recordedNoAgent ? null : Text(resolved, "agent"));
        var model = Text(ran, "model") ?? worker?.Model ?? Text(resolved, "model");
        var ranEffort = Text(ran, "effort") ?? runnerEffort;
        var effort = ranEffort ?? Text(resolved, "effort");

        var configured = resolved is null
            ? null
            : new DeliveryRunStageConfiguration(
                Text(resolved, "agent"),
                Text(resolved, "model"),
                Text(resolved, "effort"),
                Text(resolved, "origin") ?? Text(resolved, "source") ?? layerOrigin);

        var differences = new List<DeliveryRunStageDifference>();

        if (configured is not null && !isGate && !IsPending(stage.Status))
        {
            if (configured.Agent is not null && !string.Equals(agent, configured.Agent, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add(new(DeliveryRunStageField.Agent, configured.Agent, agent));
            }

            if (configured.Model is not null && model is not null && Family(model) != Family(configured.Model))
            {
                differences.Add(new(DeliveryRunStageField.Model, configured.Model, model));
            }

            if (configured.Effort is not null && ranEffort is not null && !string.Equals(effort, configured.Effort, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add(new(DeliveryRunStageField.Effort, configured.Effort, effort));
            }
        }

        return new DeliveryRunStageExecution(
            Name: stage.Name,
            Status: stage.Status,
            Mode: mode,
            ModeRecorded: recorded is not null || isGate,
            Agent: agent,
            Model: model,
            Effort: effort,
            Skill: Text(execution, "skill"),
            Mcp: execution.TryGetPropertyValue("mcp", out var mcp) ? (mcp is null ? [] : Names(mcp)) : null,
            Before: Names(execution["before"]),
            After: Names(execution["after"]),
            Fallback: Text(execution, "fallback"),
            Configured: configured,
            Differences: differences,
            Workers: workers,
            Passes: stage.DoneCount,
            DurationMs: stage.DurationMs,
            OutputTokens: run.TokenUsage?.ByStage.FirstOrDefault(usage => string.Equals(usage.StageName, stage.Name, StringComparison.Ordinal))?.Total.OutputTokens,
            ToolCalls: stage.ToolCalls,
            RunRecordsPhases: map is { Count: > 0 });
    }

    /// <summary>
    /// The sub-agent calls a stage made: each one the run's insights recorded, with its
    /// own time, tokens and tool calls; where the insights recorded none, each call the
    /// stage's execution listed under <c>runs</c> — the owner's own slices excepted;
    /// then each agent the stage declared that neither shows running.
    /// </summary>
    private static List<DeliveryRunStageWorker> Workers(DeliveryRunStage stage, JsonObject ran, bool isGate)
    {
        var workers = stage.SubAgentRuns
            .Select(call => new DeliveryRunStageWorker(call.Agent, call.Model, null, null, call.DurationMs, call.Tokens, call.ToolCalls, call.Failed, false, isGate, true) { EndedAt = call.EndedAt })
            .ToList();

        if (workers.Count == 0 && ran["runs"] is JsonArray calls)
        {
            foreach (var call in calls.OfType<JsonObject>())
            {
                if (Text(call, "agent") is not { } name || OwnerWords.Contains(name)) continue;

                workers.Add(new DeliveryRunStageWorker(name, Text(call, "model"), Text(call, "effort"), Text(call, "slice"), null, null, null, false, false, isGate, false));
            }
        }

        foreach (var declared in stage.Agents.Where(agent => agent.Count == 0))
        {
            if (workers.Any(worker => SameAgent(worker.Agent, declared.Name))) continue;

            workers.Add(new DeliveryRunStageWorker(declared.Name, declared.Model, null, null, null, null, null, false, true, isGate, false));
        }

        return workers;
    }

    /// <summary>The words a stage's <c>runs</c> uses for the owner session's own
    /// slices, which are not sub-agents.</summary>
    private static readonly HashSet<string> OwnerWords = new(["owner", "owner session", "main", "main session", "session", "self", "inline"], StringComparer.OrdinalIgnoreCase);

    private static bool SameAgent(string recorded, string declared) =>
        string.Equals(recorded, declared, StringComparison.OrdinalIgnoreCase)
        || declared.EndsWith($":{recorded}", StringComparison.OrdinalIgnoreCase)
        || recorded.EndsWith($":{declared}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The worker that did the phase's work: the bound agent when it ran, or an effort
    /// runner carrying it, else the longest-running.
    /// </summary>
    private static DeliveryRunStageWorker? BoundWorker(List<DeliveryRunStageWorker> workers, string? agent) =>
        workers.FirstOrDefault(worker => agent is not null && string.Equals(worker.Agent, agent, StringComparison.OrdinalIgnoreCase))
        ?? workers.FirstOrDefault(worker => RunnerEffort(worker.Agent) is not null)
        ?? workers.OrderByDescending(worker => worker.DurationMs ?? 0).FirstOrDefault();

    /// <summary>The effort an effort runner's name carries: <c>delivery:runner-high</c>
    /// is high.</summary>
    private static string? RunnerEffort(string agent) =>
        Runner().Match(agent) is { Success: true } match ? match.Groups["effort"].Value : null;

    private static DeliveryRunStageMode? ModeOf(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "inline" => DeliveryRunStageMode.Inline,
        "delegate" or "delegated" => DeliveryRunStageMode.Delegate,
        "fork" or "forked" => DeliveryRunStageMode.Fork,
        "gate" => DeliveryRunStageMode.Gate,
        _ => null
    };

    /// <summary>Whether a status word is one nobody has reported on yet — anything that
    /// is not done, under way, waiting, blocked or skipped.</summary>
    private static bool IsPending(string status)
    {
        var word = StatusNoise().Replace(status.ToLowerInvariant(), string.Empty);

        return word is not ("done" or "completed" or "complete" or "passed" or "approved" or "success"
            or "awaitingapproval" or "waiting" or "pendingapproval"
            or "inprogress" or "running" or "active" or "started"
            or "blocked" or "failed" or "error" or "rejected" or "aborted"
            or "skipped");
    }

    /// <summary>
    /// The run context's phase map for the run's flow: <c>phases.&lt;flow&gt;</c>, or
    /// <c>phases</c> itself where a writer recorded it flat.
    /// </summary>
    private static JsonObject? PhaseMap(JsonObject? context, string skillId)
    {
        if (context?["phases"] is not JsonObject phases) return null;

        if (!string.IsNullOrWhiteSpace(skillId) && phases[skillId] is JsonObject nested) return nested;

        // A flat map holds phase entries; a map keyed by some other flow does not
        // describe this run.
        var entries = new JsonObject();
        foreach (var (key, value) in phases)
        {
            if (value is JsonObject entry && !key.StartsWith("flow-", StringComparison.OrdinalIgnoreCase)) entries[key] = entry.DeepClone();
        }

        return entries.Count == 0 ? null : entries;
    }

    /// <summary>
    /// The stage's entry in the phase map: the qualifier it ran under first, then the
    /// bare key under any of the names a stage title goes by, then a qualified entry
    /// when it is the only one.
    /// </summary>
    private static JsonObject? Entry(JsonObject? map, string name, string? qualifier)
    {
        if (map is null) return null;

        var keys = PhaseKeys(name);
        var phaseKey = keys[^1];

        if (qualifier is not null && map[$"{phaseKey}:{qualifier}"] is JsonObject underQualifier) return underQualifier;

        foreach (var key in keys)
        {
            if (map[key] is JsonObject exact) return exact;
        }

        var qualified = map.Where(pair => pair.Key.StartsWith($"{phaseKey}:", StringComparison.Ordinal) && pair.Value is JsonObject).ToList();

        return qualified.Count == 1 ? (JsonObject)qualified[0].Value! : null;
    }

    /// <summary>Stage titles whose phase skill is not their slug, and the stage names an
    /// engine before 1.18.0 used.</summary>
    private static readonly Dictionary<string, string> PhaseAliases = new(StringComparer.Ordinal)
    {
        ["create-pull-request"] = "create-pr",
        ["scope-discovery"] = "scope",
        ["implementation"] = "implement",
        ["validation"] = "verify",
        ["verification"] = "spec-check",
        ["work-item-update"] = "report-back"
    };

    /// <summary>The names a stage's entry may be filed under: the title, its slug, the
    /// phase id and <c>phase-&lt;id&gt;</c>, the last always last.</summary>
    private static string[] PhaseKeys(string name)
    {
        var slug = SlugNoise().Replace(name.ToLowerInvariant().Replace('&', ' '), "-").Trim('-');
        var phase = PhaseAliases.GetValueOrDefault(slug, slug);

        return [name, slug, phase, $"phase-{phase}"];
    }

    /// <summary>Where the configuration came from, as the run context's <c>layers</c>
    /// says: the team default alone, or the team default with the overlays it lists
    /// merged over it. Null where the run recorded no layers.</summary>
    private static string? LayerOrigin(JsonObject? context)
    {
        if (context?["layers"] is not JsonArray layers) return null;

        // A layer is its name, or the checker's own record of one — { scope, path,
        // present } — which names an overlay that is there by its scope.
        var named = new List<string>();

        foreach (var layer in layers)
        {
            if (layer is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                named.Add(text.Trim());
            }
            else if (layer is JsonObject record
                && !(record["present"] is JsonValue present && present.TryGetValue<bool>(out var isPresent) && !isPresent))
            {
                if (Text(record, "scope") is { } scope) named.Add($"{scope} overlay");
                else if ((Text(record, "name") ?? Text(record, "id")) is { } name) named.Add(name);
            }
        }

        return named.Count == 0 ? "team default" : $"team default, merged with {string.Join(", ", named)}";
    }

    private static IReadOnlyList<string> Names(JsonNode? node)
    {
        if (node is not JsonArray array) return [];

        var names = new List<string>();

        foreach (var item in array)
        {
            var name = item switch
            {
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                JsonObject entry => Text(entry, "skill") ?? Text(entry, "id") ?? Text(entry, "name"),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
        }

        return names;
    }

    private static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    /// <summary>A JSON object out of its text, or null for anything else — a stage
    /// that recorded no execution, or one a hand-edit broke.</summary>
    private static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("personal validation", RegexOptions.IgnoreCase)]
    private static partial Regex GateName();

    [GeneratedRegex("^delivery:runner-(?<effort>[a-z]+)$")]
    private static partial Regex Runner();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugNoise();

    [GeneratedRegex(@"[\s_-]")]
    private static partial Regex StatusNoise();
}
