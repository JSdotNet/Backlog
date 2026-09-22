using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// The delivery runs a machine can account for, read from what the two dashboard
/// servers leave in the user profile.
/// <para>
/// Two dashboards, one layout. The <c>claude-desktop</c> plugin's orch-dashboard and
/// the <c>delivery-surface-dashboard</c> that succeeded it both keep run state out of
/// the repository — a worktree is thrown away after its pull request, and run files
/// are not something anyone wants in <c>git status</c> — and both key it the same way:
/// </para>
/// <list type="bullet">
/// <item><description><c>&lt;dashboard&gt;/&lt;slug&gt;-&lt;hash&gt;/runs/*.json</c>
/// — one file per run, where the folder is the worktree's leaf plus eight hex
/// characters of a hash of its full path. The hash is one-way, so the path is not
/// recoverable from here; the folder name is the key this reader hands on, and
/// <see cref="DeliveryRunWorktrees"/> derives the same key from a session's folder
/// so the list can put the two together.</description></item>
/// </list>
/// <para>
/// A run file names no session, and this reader does not pretend otherwise: it hands
/// on the worktree key and the times, and which session a run joins is
/// <see cref="SessionRows"/>' decision, made where both readings are in hand.
/// </para>
/// <para>
/// Failure is proportionate to what failed. A corrupt run file — and there is one on
/// the profile this was written against, cut off mid-write — costs that run and
/// nothing else. A folder whose <c>runs</c> cannot be enumerated is named on the
/// unreadable list and the other folders still read. A dashboard root that is not
/// there is the ordinary case for a machine that never installed that plugin and is
/// not reported at all.
/// </para>
/// </summary>
internal sealed partial class DeliveryRunReader
{
    /// <summary>The dashboards this reader knows the layout of, by the folder each
    /// keeps under the profile. The plan that asked for this reader named the second
    /// <c>delivery-dashboard</c>; the server that ships writes
    /// <c>delivery-surface-dashboard</c>, and a folder nothing writes is not worth a
    /// read.</summary>
    internal static readonly string[] Dashboards = ["orch-dashboard", "delivery-surface-dashboard"];

    private readonly string _home;
    private readonly string _environmentId;
    private readonly string _environment;

    /// <summary>The profile folder the dashboards write under, normally
    /// <c>~/.claude</c>, named so a test can point the reader at a fixture; and the
    /// environment to stamp every run with, an id and a name, for the reason the
    /// session readers take one — a run file found here was written here.</summary>
    internal DeliveryRunReader(string home, string environmentId, string environment)
    {
        _home = home;
        _environmentId = environmentId;
        _environment = environment;
    }

    internal async Task<DeliveryRunCatalog> ReadAsync(CancellationToken cancellationToken)
    {
        var runs = new List<DeliveryRun>();
        var unreadable = new List<string>();

        foreach (var dashboard in Dashboards)
        {
            var root = new DirectoryInfo(Path.Combine(_home, dashboard));

            if (!root.Exists) continue;

            DirectoryInfo[] worktrees;

            try
            {
                worktrees = root.GetDirectories();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(dashboard);

                continue;
            }

            foreach (var worktree in worktrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ReadWorktreeAsync(dashboard, worktree, runs, unreadable, cancellationToken).ConfigureAwait(false);
            }
        }

        return new DeliveryRunCatalog(runs, unreadable);
    }

    private async Task ReadWorktreeAsync(
        string dashboard,
        DirectoryInfo worktree,
        List<DeliveryRun> runs,
        List<string> unreadable,
        CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(Path.Combine(worktree.FullName, "runs"));

        if (!folder.Exists) return;

        FileInfo[] files;

        try
        {
            files = folder.GetFiles("*.json");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The worktree's own folder name, not the dashboard's: the surface can
            // then say which worktree's runs are missing rather than blaming the
            // whole dashboard for one folder.
            unreadable.Add($"{dashboard}/{worktree.Name}");

            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var run = await ReadRunAsync(dashboard, worktree.Name, file, cancellationToken).ConfigureAwait(false);

            if (run is not null) runs.Add(run);
        }
    }

    /// <summary>
    /// One run file, or null when it is not one. Null rather than a throw for the
    /// reason the live-session reader gives: the file is written by another process,
    /// a half-written one is an ordinary event, and losing one row is the
    /// proportionate answer to it.
    /// </summary>
    private async Task<DeliveryRun?> ReadRunAsync(
        string dashboard,
        string worktree,
        FileInfo file,
        CancellationToken cancellationToken)
    {
        string json;

        try
        {
            json = await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) return null;

            // The id is the one field without which the record is not a run: it is
            // what the dashboard resumes by and what a row is keyed on. The file's own
            // name is deliberately not a fallback — a file called run-x.json holding a
            // run whose id says otherwise is a file this reader does not understand.
            var id = Text(root, "id");

            if (string.IsNullOrWhiteSpace(id)) return null;

            var fileWritten = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

            return new DeliveryRun(
                Id: id,
                Dashboard: dashboard,
                Worktree: worktree,
                WorktreeName: WorktreeName(worktree),
                EnvironmentId: _environmentId,
                Environment: _environment,
                SkillId: Text(root, "skillId") ?? string.Empty,
                Title: Text(root, "title") ?? id,
                Status: Text(root, "status") ?? string.Empty,
                ChangeKind: Text(root, "changeKind"),
                References: References(root),
                StartedAt: Moment(root, "startedAt"),
                UpdatedAt: Moment(root, "updatedAt") ?? fileWritten,
                Stages: Stages(root),
                TokenUsage: TokenUsage(root),
                Context: Context(root),
                InsightsByCategory: Insights(root, ByCategory),
                InsightsByServer: Insights(root, ByServer));
        }
    }

    /// <summary>Whether a stage name is one: not blank, and not the word a
    /// JavaScript writer uses for a value it did not have.</summary>
    private static bool Named([NotNullWhen(true)] string? name) =>
        !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "undefined", StringComparison.Ordinal);

    /// <summary>The folder name with its hash suffix removed. The hash is always the
    /// last eight hex characters after a hyphen; the slug before it may itself hold
    /// hyphens, which is why this is a suffix match and not a split.</summary>
    internal static string WorktreeName(string worktree) => HashSuffix().Replace(worktree, string.Empty);

    [GeneratedRegex("-[0-9a-f]{8}$")]
    private static partial Regex HashSuffix();

    /// <summary>
    /// What the run is linked to, gathered from the three places a run file says so
    /// and deduplicated: its own <c>githubIssue</c> field, the links its stages
    /// recorded, and the Backlog plan item its prompt names.
    /// <para>
    /// Ordered by what a reader asks first — what the run was for, then what it
    /// produced — rather than by where in the file each was found. Within a kind the
    /// order is the file's.
    /// </para>
    /// <para>
    /// The stage links are where the pull requests are: a run records the pull request
    /// it opened as a link on the stage that opened it, and the dashboard's tracker
    /// field holds only the item it started from. The same links also hold the
    /// dashboards and harness URLs a run used, which are dead ports on the next
    /// machine and are left out here — only a github.com issue or pull-request address
    /// becomes a reference.
    /// </para>
    /// </summary>
    private static IReadOnlyList<DeliveryRunReference> References(JsonElement root)
    {
        var found = new List<DeliveryRunReference>();

        if (root.TryGetProperty("githubIssue", out var issue) && issue.ValueKind is JsonValueKind.Object)
        {
            var url = Text(issue, "url");
            var number = Integer(issue, "number");

            if (url is not null || number is not null)
            {
                var kind = KindOf(url);

                found.Add(new DeliveryRunReference(
                    kind,
                    number is { } value ? Label(kind, (int)value) : "GitHub",
                    Text(issue, "title"),
                    url,
                    Text(issue, "repo") ?? RepositoryOf(url)));
            }
        }

        if (root.TryGetProperty("stages", out var stages) && stages.ValueKind is JsonValueKind.Array)
        {
            foreach (var stage in stages.EnumerateArray())
            {
                if (stage.ValueKind is not JsonValueKind.Object) continue;
                if (!stage.TryGetProperty("links", out var links) || links.ValueKind is not JsonValueKind.Array) continue;

                foreach (var link in links.EnumerateArray())
                {
                    if (link.ValueKind is not JsonValueKind.Object) continue;

                    var url = Text(link, "url");

                    if (GitHubItem().Match(url ?? string.Empty) is not { Success: true, Index: 0 } match) continue;

                    var kind = KindOf(url);
                    var number = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var title = Text(link, "description");

                    found.Add(new DeliveryRunReference(
                        kind,
                        Label(kind, number),
                        string.IsNullOrWhiteSpace(title) ? null : title,
                        url,
                        $"{match.Groups[1].Value}/{match.Groups[2].Value}"));
                }
            }
        }

        // The run's own closing summary, last: a run that opened a pull request
        // often states its address there and nowhere else — 62 of the 325 files on
        // the profile this was written against name one only here. An address
        // somebody wrote down is a recorded fact rather than an inference, and the
        // summary is the one prose field that is about what the run delivered, which
        // is why it is read and the stage outputs are not.
        foreach (Match match in GitHubItem().Matches(Text(root, "summary") ?? string.Empty))
        {
            var kind = KindOf(match.Value);

            found.Add(new DeliveryRunReference(
                kind,
                Label(kind, int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)),
                Title: null,
                match.Value,
                $"{match.Groups[1].Value}/{match.Groups[2].Value}"));
        }

        if (PlanItem(root) is { } task) found.Add(task);

        return
        [
            .. found
                // Keyed by what the reference is, not by where it was found: a run's
                // own tracker field and a stage link can name the same pull request,
                // and one pull request is one reference. The first wins, which is the
                // one with the better title: the tracker field carries the item's own,
                // a stage link carries whatever the run wrote beside it.
                .GroupBy(reference => (reference.Kind, reference.Repository, reference.Label), TupleComparer)
                .Select(group => group.First())
                .OrderBy(reference => reference.Kind)
        ];
    }

    private static readonly IEqualityComparer<(DeliveryRunReferenceKind Kind, string? Repository, string Label)> TupleComparer =
        EqualityComparer<(DeliveryRunReferenceKind, string?, string)>.Default;

    /// <summary>
    /// The Backlog entry the run was started from, where its prompt names one. The
    /// marker is the product's own convention for a pasted plan item
    /// (<c>.design/content-editing.md</c> and the import plan's grammar), and it is
    /// the only thing in a run file that names a Backlog entry — so it is read rather
    /// than guessed, and a run started any other way simply has none.
    /// </summary>
    private static DeliveryRunReference? PlanItem(JsonElement root)
    {
        var prompt = Text(root, "originalPrompt");

        if (string.IsNullOrWhiteSpace(prompt)) return null;

        if (PlanMarker().Match(prompt) is not { Success: true } match) return null;

        return new DeliveryRunReference(
            DeliveryRunReferenceKind.Task,
            match.Groups[1].Value,
            $"Plan {match.Groups[2].Value}",

            // No address: a Backlog entry is in this product, not on a page. A
            // surface that can open one wires that itself, and needs the plan as
            // well as the id to find it.
            Url: null,
            Repository: null,
            Plan: match.Groups[2].Value);
    }

    /// <summary>A pull request when the address says so, an issue otherwise: a
    /// dashboard files both under one field and only the path tells them apart.</summary>
    private static DeliveryRunReferenceKind KindOf(string? url) =>
        url?.Contains("/pull/", StringComparison.OrdinalIgnoreCase) is true
            ? DeliveryRunReferenceKind.PullRequest
            : DeliveryRunReferenceKind.Issue;

    /// <summary>What the reference is called: <c>#128</c> for an issue and
    /// <c>PR #74</c> for a pull request, which is how this product's own integration
    /// references are labelled.</summary>
    private static string Label(DeliveryRunReferenceKind kind, int number) =>
        kind is DeliveryRunReferenceKind.PullRequest ? $"PR #{number}" : $"#{number}";

    private static string? RepositoryOf(string? url) =>
        GitHubItem().Match(url ?? string.Empty) is { Success: true, Index: 0 } match
            ? $"{match.Groups[1].Value}/{match.Groups[2].Value}"
            : null;

    [GeneratedRegex(@"https?://github\.com/([\w.-]+)/([\w.-]+)/(?:pull|issues)/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubItem();

    [GeneratedRegex("""Backlog plan item [`'"]([\w.-]+)[`'"] of plan [`'"]([\w.-]+)[`'"]""")]
    private static partial Regex PlanMarker();

    /// <summary>
    /// The stages, named. An older generation of the dashboard stored them nameless —
    /// <c>start_run</c> kept the list and the names went to <c>phaseDoneCounts</c>,
    /// whose keys are in stage order — so a nameless stage takes the key at its own
    /// position, and only a stage with neither gets a positional stand-in.
    /// </summary>
    private static IReadOnlyList<DeliveryRunStage> Stages(JsonElement root)
    {
        if (!root.TryGetProperty("stages", out var stages) || stages.ValueKind is not JsonValueKind.Array) return [];

        var phaseNames = root.TryGetProperty("phaseDoneCounts", out var phases) && phases.ValueKind is JsonValueKind.Object
            ? phases.EnumerateObject().Select(phase => phase.Name).ToList()
            : [];

        // A list that is not in stage order is not a list of stage names. Found on a
        // real profile: a writer that lost its stage names once filed every count
        // under the key "undefined" — one key for six stages — and the third stage
        // of that run came out named after JavaScript's missing value. A key that is
        // that word, or blank, is an artefact of the writer rather than a name, and
        // once the list holds one nothing about its positions can be trusted.
        if (phaseNames.Any(name => !Named(name))) phaseNames = [];

        var read = new List<DeliveryRunStage>();
        var index = 0;

        foreach (var stage in stages.EnumerateArray())
        {
            var position = index++;

            if (stage.ValueKind is not JsonValueKind.Object) continue;

            var name = Text(stage, "name");

            if (!Named(name))
            {
                name = position < phaseNames.Count ? phaseNames[position] : $"Stage {position + 1}";
            }

            read.Add(new DeliveryRunStage(
                name,
                Text(stage, "status") ?? string.Empty,
                Integer(stage, "durationMs"),
                (int)(Integer(stage, "doneCount") ?? 0)));
        }

        return read;
    }

    private static DeliveryRunTokenUsage? TokenUsage(JsonElement root)
    {
        if (!root.TryGetProperty("tokenUsage", out var usage) || usage.ValueKind is not JsonValueKind.Object) return null;

        var byStage = new List<DeliveryRunStageTokens>();

        if (usage.TryGetProperty("byStage", out var stages) && stages.ValueKind is JsonValueKind.Object)
        {
            // Keyed by stage index as strings, and written in the order the stages
            // were reached — which is stage order. Sorted numerically anyway rather
            // than trusting the writer's property order, because "10" before "2" is
            // exactly the mistake an ordinal sort would make on a run long enough to
            // have ten stages.
            foreach (var stage in stages.EnumerateObject().OrderBy(stage => int.TryParse(stage.Name, out var n) ? n : int.MaxValue))
            {
                if (stage.Value.ValueKind is not JsonValueKind.Object) continue;

                // The stage's own name where it has one; its position where it is
                // blank, which the same writer that loses stage names also does. The
                // key is a zero-based index and the stage list above numbers from one,
                // so the two agree only with the one added — found by QA, where "Stage
                // 1" in the token lines was the list's "Stage 2".
                var stageName = Text(stage.Value, "stageName");
                var label = Named(stageName)
                    ? stageName
                    : int.TryParse(stage.Name, out var index) ? $"Stage {index + 1}" : $"Stage {stage.Name}";

                byStage.Add(new DeliveryRunStageTokens(
                    label,
                    Tokens(stage.Value, "total"),
                    Tokens(stage.Value, "subAgent")));
            }
        }

        var models = usage.TryGetProperty("models", out var list) && list.ValueKind is JsonValueKind.Array
            ? list.EnumerateArray().Where(model => model.ValueKind is JsonValueKind.String).Select(model => model.GetString()!).ToList()
            : [];

        return new DeliveryRunTokenUsage(Tokens(usage, "total"), Tokens(usage, "subAgent"), byStage, models);
    }

    private static DeliveryRunTokens Tokens(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var bucket) || bucket.ValueKind is not JsonValueKind.Object)
        {
            return new DeliveryRunTokens(0, 0, 0, 0, 0, 0);
        }

        return new DeliveryRunTokens(
            Integer(bucket, "modelCalls") ?? 0,
            Integer(bucket, "inputTokens") ?? 0,
            Integer(bucket, "outputTokens") ?? 0,
            Integer(bucket, "reasoningTokens") ?? 0,
            Integer(bucket, "cacheReadTokens") ?? 0,
            Integer(bucket, "cacheWriteTokens") ?? 0);
    }

    private static DeliveryRunContext? Context(JsonElement root)
    {
        if (!root.TryGetProperty("context", out var context) || context.ValueKind is not JsonValueKind.Object) return null;

        return new DeliveryRunContext(
            Integer(context, "currentTokens") ?? 0,
            Integer(context, "tokenLimit") ?? 0,
            Integer(context, "peakTokens") ?? 0);
    }

    /// <summary>
    /// The insight list summed by one key, largest group first. Summed here rather than
    /// carried, and that is a deliberate loss: the largest file on this machine holds a
    /// thousand insight records of one tool call each, and a row wants "31,639 shell
    /// calls, 2 hours" rather than the list. Anything a surface would want to say about
    /// them is a total per category or per server, and both are produced from one
    /// pass.
    /// </summary>
    private static IReadOnlyList<DeliveryRunInsightGroup> Insights(JsonElement root, Func<JsonElement, string?> key)
    {
        if (!root.TryGetProperty("insights", out var insights) || insights.ValueKind is not JsonValueKind.Array) return [];

        var groups = new Dictionary<string, (int Count, long Duration, int Failed)>(StringComparer.Ordinal);

        foreach (var insight in insights.EnumerateArray())
        {
            if (insight.ValueKind is not JsonValueKind.Object) continue;

            var name = key(insight);

            if (name is null) continue;

            var current = groups.GetValueOrDefault(name);

            groups[name] = (
                current.Count + 1,
                current.Duration + (Integer(insight, "durationMs") ?? 0),
                current.Failed + (Failed(insight) ? 1 : 0));
        }

        return
        [
            .. groups
                .Select(group => new DeliveryRunInsightGroup(group.Key, group.Value.Count, group.Value.Duration, group.Value.Failed))
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.Name, StringComparer.Ordinal)
        ];
    }

    /// <summary>Every insight has a category: a tool call carries the dashboard's,
    /// and a delegated agent — which the dashboard files without one — is its own.</summary>
    private static string ByCategory(JsonElement insight) =>
        Text(insight, "category") ?? (Text(insight, "kind") is "agent" ? "Agents" : "Other");

    /// <summary>Only an MCP tool call has a server; a built-in tool is null here and
    /// therefore in no group, which is what "by MCP server" means.</summary>
    private static string? ByServer(JsonElement insight) =>
        Text(insight, "mcpServerName") is { Length: > 0 } server ? server : null;

    /// <summary>A tool call that reported <c>success: false</c>, or an agent that did
    /// not complete. A record that says neither is not counted as failed — absence of
    /// the flag is not a verdict.</summary>
    private static bool Failed(JsonElement insight)
    {
        if (insight.TryGetProperty("success", out var success)) return success.ValueKind is JsonValueKind.False;

        return Text(insight, "kind") is "agent" && Text(insight, "status") is { } status
            && !string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ISO-8601 text, which is how the dashboards date everything. Null for
    /// a missing or unparseable value rather than a default date a reader would take
    /// for a real one.</summary>
    private static DateTimeOffset? Moment(JsonElement root, string name) =>
        Text(root, name) is { } text && DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var moment)
            ? moment
            : null;

    /// <summary>A whole number, or a fractional one rounded — a duration measured in
    /// milliseconds is occasionally written with a fraction, and losing half a
    /// millisecond is better than losing the stage.</summary>
    private static long? Integer(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is not JsonValueKind.Number) return null;

        if (value.TryGetInt64(out var number)) return number;

        return value.TryGetDouble(out var fraction) && double.IsFinite(fraction) ? (long)Math.Round(fraction) : null;
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
