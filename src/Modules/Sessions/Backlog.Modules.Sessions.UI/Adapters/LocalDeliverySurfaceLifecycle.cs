using System.Globalization;
using System.Text.Json.Nodes;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// The <c>delivery.surface.lifecycle@1</c> capability over the run files under this
/// machine's profile.
/// <para>
/// Reading and writing are deliberately asymmetric here. Writing goes through
/// <see cref="DeliveryRunStore"/>, which touches one folder; reading —
/// <c>list_runs</c> and <c>get_run</c> — goes back through
/// <see cref="DeliveryRunReader"/>, the same reader the pane uses, and filters its
/// answer to the worktree asked about. It would be cheaper to project the node
/// documents this class just wrote, and that is exactly the reason not to: a caller
/// asking what a run looks like is asking what the pane shows, and a second projection
/// would be a second opinion about that, free to drift from the first without anything
/// failing.
/// </para>
/// </summary>
internal sealed class LocalDeliverySurfaceLifecycle : IDeliverySurfaceLifecycle
{
    private readonly DeliveryRunStore _store;
    private readonly DeliveryRunReader _reader;
    private readonly ISessionsSurfaceActivator? _shell;

    /// <summary>What a host composes: the dashboards' folders under the signed-in
    /// profile, this device's identity, and whatever is showing the application —
    /// which may be nothing, on a host that composed the port without a window.</summary>
    internal LocalDeliverySurfaceLifecycle(IDeviceIdentitySource identity, ISessionsSurfaceActivator? shell = null)
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            IdOf(identity),
            identity.Current.Name,
            shell)
    {
    }

    /// <summary>Every input named, so the writing can be tested against a fixture —
    /// the shape <see cref="LocalDeliveryRunSource"/> has, for the same reason.</summary>
    internal LocalDeliverySurfaceLifecycle(
        string home,
        string environmentId,
        string environment,
        ISessionsSurfaceActivator? shell = null)
    {
        _store = new DeliveryRunStore(home);
        _reader = new DeliveryRunReader(home, environmentId, environment);
        _shell = shell;
    }

    private static string IdOf(IDeviceIdentitySource identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return identity.Current.Id.ToString();
    }

    public async Task<DeliverySurfaceOpened> OpenDashboardAsync(CancellationToken cancellationToken = default)
    {
        var activation = _shell is null
            ? DeliverySurfaceActivation.Unattached
            : await _shell.ActivateAsync(cancellationToken).ConfigureAwait(false);

        // No URL, in any branch. The engine's reporting contract asks a surface to be
        // surfaced — inline where the host renders it, as a link otherwise — and this
        // host renders it inline by being the application. A caller that was handed an
        // address here would open a second window onto the thing it is already talking
        // to.
        return new DeliverySurfaceOpened(activation, activation switch
        {
            DeliverySurfaceActivation.Shown =>
                "Backlog is showing the Sessions pane; this run appears there as it is recorded.",
            DeliverySurfaceActivation.Disabled =>
                "Backlog is open but the Sessions area is switched off in Settings, so the run is recorded without a pane to watch it in.",
            _ =>
                "Backlog is not showing a window, so there is nothing to bring forward; the run is recorded and appears in the Sessions pane when it is opened."
        });
    }

    public async Task<DeliveryRunStarted> StartRunAsync(
        string worktree,
        string skillId,
        string title,
        IReadOnlyList<string> stages,
        string? changeKind = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ArgumentNullException.ThrowIfNull(stages);

        // Reattach before starting. The engine calls start_run on every session that
        // picks the work back up, and a second file for the same skill in the same
        // worktree would not be a second run — it would be the same run, listed twice,
        // with the stages split between the two.
        if (await LiveRunAsync(worktree, skillId, cancellationToken).ConfigureAwait(false) is { } resumed)
        {
            var id = Text(resumed, "id")!;

            return new DeliveryRunStarted(id, Resumed: true, SessionTitle(resumed));
        }

        var runId = NewRunId();
        var now = Now();

        var run = new JsonObject
        {
            ["id"] = runId,
            ["skillId"] = skillId,
            ["title"] = string.IsNullOrWhiteSpace(title) ? skillId : title,
            ["status"] = DeliveryRunStatuses.InProgress,
            ["changeKind"] = changeKind,
            ["approval"] = new JsonObject
            {
                ["personalValidation"] = null,
                ["decidedAt"] = null,
                ["note"] = null
            },
            ["originalPrompt"] = string.Empty,
            ["promptHistory"] = new JsonArray(),
            ["githubIssue"] = null,
            ["startedAt"] = now,
            ["updatedAt"] = now,

            // The whole list at once, every stage pending. A surface that grew its
            // stage list one call at a time could never show what is still to come,
            // and update_stage addressing a stage by index only means anything
            // against a list that was fixed when the run began.
            ["stages"] = new JsonArray([.. stages.Select(Pending)]),
            ["summary"] = string.Empty
        };

        WritePhaseDoneCounts(run);

        await _store.WriteAsync(worktree, runId, run, cancellationToken).ConfigureAwait(false);

        return new DeliveryRunStarted(runId, Resumed: false, SessionTitle(run));
    }

    public async Task RecordPromptAsync(
        string worktree,
        string runId,
        string prompt,
        string? kind = null,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var run = await RequiredAsync(worktree, runId, cancellationToken).ConfigureAwait(false);

        var history = run["promptHistory"] as JsonArray;

        if (history is null)
        {
            history = [];
            run["promptHistory"] = history;
        }

        var first = history.Count == 0;

        history.Add(new JsonObject
        {
            ["kind"] = kind ?? (first ? "initial" : "follow-up"),
            ["label"] = label ?? (first ? "Initial prompt" : $"Prompt {history.Count + 1}"),
            ["prompt"] = prompt,
            ["createdAt"] = Now()
        });

        // The first prompt is also the run's originalPrompt, which is not bookkeeping:
        // it is the field the reader scans for a Backlog plan-item marker, and so the
        // only reason a run ever links back to the entry it was started from.
        if (first) run["originalPrompt"] = prompt;

        await SaveAsync(worktree, runId, run, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetRunContextAsync(
        string worktree,
        string runId,
        string? changeKind = null,
        string? approval = null,
        string? approvalNote = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var run = await RequiredAsync(worktree, runId, cancellationToken).ConfigureAwait(false);

        // Every argument absent leaves what was there. This is called once per fact as
        // the run learns it — the change kind in Stage 0, the approval at each gate,
        // the model when it resolves — and a call that nulled the two it was not about
        // would erase the gate decision every time the model was recorded.
        if (changeKind is not null) run["changeKind"] = changeKind;

        if (approval is not null || approvalNote is not null)
        {
            if (run["approval"] is not JsonObject decided)
            {
                decided = [];
                run["approval"] = decided;
            }

            if (approval is not null)
            {
                decided["personalValidation"] = approval;
                decided["decidedAt"] = Now();
            }

            if (approvalNote is not null) decided["note"] = approvalNote;
        }

        if (model is not null)
        {
            if (run["tokenUsage"] is not JsonObject usage)
            {
                usage = [];
                run["tokenUsage"] = usage;
            }

            // The models list and nothing else. The rest of tokenUsage is measured
            // consumption, which this product cannot measure: the figures are captured
            // by a collector watching the session's own tool calls, and this server
            // sees a tool call arrive, not the session that made it. An absent bucket
            // reads as zero, which is true; an invented one would not be.
            var models = usage["models"] as JsonArray;

            if (models is null)
            {
                models = [];
                usage["models"] = models;
            }

            if (!models.Any(known => known is JsonValue text && text.TryGetValue<string>(out var seen) && string.Equals(seen, model, StringComparison.Ordinal)))
            {
                models.Add(model);
            }
        }

        await SaveAsync(worktree, runId, run, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeliveryStageUpdated> UpdateStageAsync(
        string worktree,
        string runId,
        int stageIndex,
        string status,
        string? output = null,
        IReadOnlyList<DeliveryStageLink>? links = null,
        IReadOnlyList<DeliveryScenario>? scenarios = null,
        DeliveryMonitoring? monitoring = null,
        CancellationToken cancellationToken = default)
    {
        if (!DeliveryStageStatuses.Requestable.Contains(status, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{status}' is not a stage status. Use one of: {string.Join(", ", DeliveryStageStatuses.Requestable)}.",
                nameof(status));
        }

        var run = await RequiredAsync(worktree, runId, cancellationToken).ConfigureAwait(false);
        var stages = run["stages"] as JsonArray ?? [];

        // Out of range is refused rather than ignored. A caller that is one stage off
        // is reporting the wrong stage for the rest of the run, and a silent no-op
        // leaves it doing that with a surface that looks merely stalled.
        if (stageIndex < 0 || stageIndex >= stages.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageIndex),
                stageIndex,
                $"Run '{runId}' has {stages.Count} stage(s); there is no stage {stageIndex}.");
        }

        if (stages[stageIndex] is not JsonObject stage)
        {
            stage = Pending($"Stage {stageIndex + 1}");
            stages[stageIndex] = stage;
        }

        var now = Now();
        var done = string.Equals(status, DeliveryStageStatuses.Done, StringComparison.Ordinal);
        var doneCount = Count(stage, "doneCount") + (done ? 1 : 0);

        stage["status"] = status;
        stage["updatedAt"] = now;
        stage["doneCount"] = doneCount;

        if (output is not null) stage["output"] = output;

        if (string.Equals(status, DeliveryStageStatuses.InProgress, StringComparison.Ordinal))
        {
            // Restamped on every pass, not only the first. A stage repeated after
            // requested changes is a new attempt, and a duration measured from the
            // first attempt would report the wait for the gate as time spent working.
            stage["startedAt"] = now;
            stage["completedAt"] = null;
            stage["durationMs"] = null;
        }
        else
        {
            stage["completedAt"] = now;
            stage["durationMs"] = Elapsed(Text(stage, "startedAt"), now);
        }

        if (links is not null)
        {
            stage["links"] = new JsonArray([.. links.Select(link => (JsonNode)new JsonObject
            {
                ["label"] = link.Label,
                ["url"] = link.Url,
                ["description"] = link.Description
            })]);
        }

        if (scenarios is not null)
        {
            stage["scenarios"] = new JsonArray([.. scenarios.Select(scenario => (JsonNode)new JsonObject
            {
                ["name"] = scenario.Name,
                ["status"] = scenario.Status,
                ["notes"] = scenario.Notes,
                ["evidence"] = new JsonArray([.. (scenario.Evidence ?? []).Select(path => (JsonNode)JsonValue.Create(path)!)])
            })]);
        }

        if (monitoring is not null)
        {
            stage["monitoring"] = new JsonObject
            {
                ["summary"] = monitoring.Summary,
                ["findings"] = new JsonArray([.. (monitoring.Findings ?? []).Select(finding => (JsonNode)JsonValue.Create(finding)!)])
            };
        }

        WritePhaseDoneCounts(run);

        await SaveAsync(worktree, runId, run, cancellationToken).ConfigureAwait(false);

        return new DeliveryStageUpdated(runId, stageIndex, status, doneCount, SessionTitle(run));
    }

    public async Task FinishRunAsync(
        string worktree,
        string runId,
        string status,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        if (!DeliveryRunStatuses.IsFinal(status))
        {
            throw new ArgumentException(
                $"'{status}' is not a status a run can finish on. Use one of: {string.Join(", ", DeliveryRunStatuses.All.Where(DeliveryRunStatuses.IsFinal))}.",
                nameof(status));
        }

        var run = await RequiredAsync(worktree, runId, cancellationToken).ConfigureAwait(false);

        run["status"] = status;

        if (summary is not null) run["summary"] = summary;

        await SaveAsync(worktree, runId, run, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeliveryRun>> ListRunsAsync(string worktree, CancellationToken cancellationToken = default)
    {
        var key = DeliveryRunWorktrees.KeyOf(worktree);

        if (key is null) return [];

        var catalog = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. catalog.Runs
                .Where(run => string.Equals(run.Worktree, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(run => run.UpdatedAt)
        ];
    }

    public async Task<DeliveryRun?> GetRunAsync(string worktree, string runId, CancellationToken cancellationToken = default)
    {
        var runs = await ListRunsAsync(worktree, cancellationToken).ConfigureAwait(false);

        return runs.FirstOrDefault(run => string.Equals(run.Id, runId, StringComparison.Ordinal));
    }

    /// <summary>The run still under way for one skill in one worktree, or null. Where
    /// more than one somehow qualifies the most recently updated wins, which is the
    /// one a session that just resumed is about to continue.</summary>
    private async Task<JsonObject?> LiveRunAsync(string worktree, string skillId, CancellationToken cancellationToken)
    {
        var runs = await _store.ReadAllAsync(worktree, cancellationToken).ConfigureAwait(false);

        return runs
            .Where(run => Text(run, "id") is { Length: > 0 })
            .Where(run => string.Equals(Text(run, "skillId"), skillId, StringComparison.Ordinal))
            .Where(run => string.Equals(Text(run, "status"), DeliveryRunStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => Text(run, "updatedAt") ?? string.Empty, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private async Task<JsonObject> RequiredAsync(string worktree, string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await _store.ReadAsync(worktree, runId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"There is no run '{runId}' recorded for '{worktree}'.");
    }

    private Task SaveAsync(string worktree, string runId, JsonObject run, CancellationToken cancellationToken)
    {
        run["updatedAt"] = Now();

        return _store.WriteAsync(worktree, runId, run, cancellationToken);
    }

    /// <summary>A stage as the run begins: named, pending, and nothing claimed about
    /// it yet.</summary>
    private static JsonObject Pending(string name) => new()
    {
        ["name"] = name,
        ["status"] = DeliveryStageStatuses.Pending,
        ["output"] = string.Empty,
        ["agents"] = new JsonArray(),
        ["startedAt"] = null,
        ["completedAt"] = null,
        ["updatedAt"] = null,
        ["doneCount"] = 0,
        ["durationMs"] = null
    };

    /// <summary>
    /// The per-stage done counts, mirrored under the key the format keeps them in.
    /// <para>
    /// Redundant here, and written anyway. The reader falls back to these keys for
    /// stage names only when a stage has none, which never happens in a file this
    /// class wrote — but the field is part of the shape, and a run recorded here that
    /// is missing something every imported run carries is a difference waiting to be
    /// found by whatever reads these files next.
    /// </para>
    /// </summary>
    private static void WritePhaseDoneCounts(JsonObject run)
    {
        var counts = new JsonObject();

        foreach (var stage in (run["stages"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var name = Text(stage, "name");

            if (string.IsNullOrWhiteSpace(name)) continue;

            counts[name] = Count(stage, "doneCount");
        }

        run["phaseDoneCounts"] = counts;
    }

    /// <summary>
    /// What this session could usefully be called, or null.
    /// <para>
    /// Null is the honest answer for now, and the contract says what a caller does
    /// with it: nothing, leaving the host's own title alone. The name the engine wants
    /// here is derived from what a run has actually written to disk, which is the
    /// collector's observation rather than anything this product can see — and the
    /// contract is explicit that a caller must not compose one itself, because a
    /// second grammar in the session list is worse than no prefix at all. The same
    /// reasoning applies to the surface that would be hand-assembling it.
    /// </para>
    /// </summary>
    private static string? SessionTitle(JsonObject run) =>
        run["destinations"] is JsonObject destinations ? Text(destinations, "sessionTitle") : null;

    /// <summary>
    /// A string property, or null where it is absent or is not a string.
    /// <para>
    /// The reader has the same pair of helpers and for the same reason, which is
    /// worth repeating here because it is easy to think a writer is exempt from it:
    /// these files sit in a person's profile in plain JSON, and a hand-edit that
    /// turns a status into a number would otherwise take down every operation on
    /// that run rather than the one field.
    /// </para>
    /// </summary>
    private static string? Text(JsonObject run, string name) =>
        run[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>A whole number, or zero where it is absent or is not one.</summary>
    private static int Count(JsonObject owner, string name) =>
        owner[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    /// <summary>A run id in the shape both dashboards write: the word, the moment in
    /// base 36, and enough randomness that two runs started in the same millisecond
    /// are still two files.</summary>
    private static string NewRunId() =>
        $"run-{Base36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())}-{Base36(Random.Shared.NextInt64(0, 2_176_782_336))}";

    private static string Base36(long value)
    {
        const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

        if (value <= 0) return "0";

        var text = new Stack<char>();

        while (value > 0)
        {
            text.Push(Digits[(int)(value % 36)]);
            value /= 36;
        }

        return new string([.. text]);
    }

    /// <summary>Milliseconds between two stamps, or null where the first is missing or
    /// unreadable — an unknown duration, never a zero one.</summary>
    private static JsonNode? Elapsed(string? startedAt, string finishedAt)
    {
        if (!DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start)) return null;
        if (!DateTimeOffset.TryParse(finishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var finish)) return null;

        return JsonValue.Create((long)Math.Max(0, (finish - start).TotalMilliseconds));
    }

    /// <summary>The stamp format every run file uses: UTC, to the millisecond, with
    /// the Z the JavaScript writers put there.</summary>
    private static string Now() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
