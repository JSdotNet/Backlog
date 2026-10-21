using System.Text.Json;

namespace Backlog.Infrastructure.Devbook.Scenarios;

/// <summary>
/// The last run of one journey, as <c>run.json</c> in the journey's run folder records
/// it (<see cref="ScenarioJourney.RunFolder"/>): when, under which profile, the
/// signature of the journey text it ran, each part's outcome, and the screenshot each
/// label captured.
/// <para>
/// Three shapes are read. Version 3 is devbook's run reporter (<c>report.mjs</c>) since
/// journeys moved into scenarios files: English fields, the journey's own signature, and
/// the journey's address. Version 2 is the same reporter from when a journey was a page
/// of its own, and names that page instead. Version 1 is spec-manager's classic
/// <c>.test</c> reporter, Dutch fields and one Gherkin signature per scenario: it says
/// nothing about the journey's signature, so a version 1 run can show its outcomes and
/// screenshots but never proves the journey current — <see cref="Signature"/> is null and
/// <see cref="ScenarioEvidence.StateOf"/> reads it as stale.
/// </para>
/// </summary>
public sealed record ScenarioRun(
    int Version,
    string? Signature,
    DateTimeOffset? RanAt,
    string? Profile,
    IReadOnlyList<ScenarioRunPart> Parts,
    IReadOnlyDictionary<string, ScenarioRunShot> Shots)
{
    /// <summary>The page the run executed, repository-relative, as a version 2 run
    /// records it; null for any other version.</summary>
    public string? Page { get; init; }

    /// <summary>The journey the run executed, as its address
    /// <c>&lt;scenarios file&gt;#&lt;journey&gt;</c>, as a version 3 run records it; null
    /// for any other version.</summary>
    public string? Journey { get; init; }

    /// <summary>The part the run recorded under this anchor, or null.</summary>
    public ScenarioRunPart? Part(string anchor) =>
        Parts.FirstOrDefault(part => string.Equals(part.Anchor, anchor, StringComparison.OrdinalIgnoreCase));

    public int Failed => Parts.Count(part => part.Outcome == ScenarioOutcome.Failed);
}

/// <summary>One part's outcome in a run.</summary>
/// <param name="FailureShot">The failure screenshot's file, relative to the run's
/// folder, for a failed part.</param>
public sealed record ScenarioRunPart(string Title, string Anchor, ScenarioOutcome Outcome, long? DurationMs, string? FailureShot)
{
    /// <summary>The 1-based step the part stopped on, as a version 2 run's
    /// <c>failure.step</c> records it for a failed part. Null when the part did not
    /// fail, when the failure came before any step — the reporter writes
    /// <c>step: null</c> then — and always for a version 1 run, which records no step.</summary>
    public int? FailureStep { get; init; }

    /// <summary>What the failure said, from <c>failure.message</c>; null where
    /// <see cref="FailureStep"/> has no failure to come from.</summary>
    public string? FailureMessage { get; init; }
}

/// <summary>The screenshot a label captured: its file relative to the run's folder,
/// the part it sits in, and when it was captured — which can be an earlier run than
/// the last, for a label the last run did not reach.</summary>
public sealed record ScenarioRunShot(string Label, string File, string? Part, DateTimeOffset? RanAt);

public enum ScenarioOutcome
{
    NotRun,
    Passed,
    Failed
}

public static class ScenarioRunReader
{
    /// <summary>
    /// The run in <paramref name="json"/>, or null when there is none, it does not
    /// parse, or it carries a version this reader does not know. Null and no exception:
    /// an unreadable run must not break the page it belongs to — the page still reads,
    /// as never run.
    /// </summary>
    public static ScenarioRun? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (Number(root, "version") is (2 or 3) and var version) return ReadReporterRun(root, (int)version);
            if (Number(root, "versie") == 1) return ReadVersion1(root);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A run devbook's reporter wrote: version 2 or 3, which differ only in
    /// naming the page or the journey it executed.</summary>
    private static ScenarioRun ReadReporterRun(JsonElement root, int version)
    {
        var parts = new List<ScenarioRunPart>();
        if (root.TryGetProperty("parts", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in list.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object) continue;
                var title = Text(part, "title") ?? string.Empty;
                parts.Add(new ScenarioRunPart(
                    title,
                    Text(part, "anchor") ?? ScenarioPageParser.Slugify(title),
                    Text(part, "outcome") switch
                    {
                        "passed" => ScenarioOutcome.Passed,
                        "failed" => ScenarioOutcome.Failed,
                        _ => ScenarioOutcome.NotRun
                    },
                    Number(part, "durationMs"),
                    Text(part, "failureShot"))
                {
                    FailureStep = Failure(part) is { } failure && Number(failure, "step") is { } step and > 0 and <= int.MaxValue ? (int)step : null,
                    FailureMessage = Failure(part) is { } failed ? Text(failed, "message") : null
                });
            }
        }

        var shots = new Dictionary<string, ScenarioRunShot>(StringComparer.Ordinal);
        if (root.TryGetProperty("shots", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var shot in map.EnumerateObject())
            {
                if (shot.Value.ValueKind != JsonValueKind.Object || Text(shot.Value, "file") is not { Length: > 0 } file) continue;
                shots[shot.Name] = new ScenarioRunShot(shot.Name, file, Text(shot.Value, "part"), Time(shot.Value, "ranAt"));
            }
        }

        return new ScenarioRun(version, Text(root, "signature")?.ToLowerInvariant(), Time(root, "ranAt"), Text(root, "profile"), parts, shots)
        {
            Page = version == 2 ? Text(root, "page") : null,
            Journey = version == 3 ? Text(root, "journey") : null
        };
    }

    private static ScenarioRun ReadVersion1(JsonElement root)
    {
        var parts = new List<ScenarioRunPart>();
        var shots = new Dictionary<string, ScenarioRunShot>(StringComparer.Ordinal);
        var ranAt = Time(root, "gedraaidOp");

        if (root.TryGetProperty("scenarios", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var scenario in list.EnumerateArray())
            {
                if (scenario.ValueKind != JsonValueKind.Object) continue;
                var title = Text(scenario, "titel") ?? string.Empty;
                var slug = Text(scenario, "slug") ?? ScenarioPageParser.Slugify(title);
                parts.Add(new ScenarioRunPart(
                    title,
                    slug,
                    Text(scenario, "uitkomst") switch
                    {
                        "geslaagd" => ScenarioOutcome.Passed,
                        "gefaald" => ScenarioOutcome.Failed,
                        _ => ScenarioOutcome.NotRun
                    },
                    Number(scenario, "duurMs"),
                    null));

                if (!scenario.TryGetProperty("afdrukken", out var prints) || prints.ValueKind != JsonValueKind.Array) continue;
                foreach (var print in prints.EnumerateArray())
                {
                    if (print.ValueKind != JsonValueKind.Object) continue;
                    if (Text(print, "bestand") is not { Length: > 0 } file || Text(print, "naam") is not { Length: > 0 } label) continue;
                    // spec-manager keeps a scenario's prints in a folder named for its slug.
                    shots.TryAdd(label, new ScenarioRunShot(label, $"{slug}/{file}", slug, ranAt));
                }
            }
        }

        return new ScenarioRun(1, null, ranAt, null, parts, shots);
    }

    /// <summary>A version 2 part's <c>failure</c> object, or null — the reporter writes
    /// one only on a failed part, so its absence is the common case, not an error.</summary>
    private static JsonElement? Failure(JsonElement part) =>
        part.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object ? failure : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) switch
        {
            true when value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) => number,
            true when value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        Text(element, name) is { } text && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;
}

/// <summary>A journey's state, in the dot's precedence: never run, then stale, then
/// failed, then passed.</summary>
public enum ScenarioState
{
    NeverRun,
    Stale,
    Failed,
    Passed
}

public static class ScenarioEvidence
{
    /// <summary>
    /// The journey's state against its last run. Stale is computed, never stored: the
    /// journey's signature now against the one the run executed. A run that proves no
    /// signature — version 1 — is stale. Otherwise any part that did not pass, or a
    /// part of the journey the run does not mention, is a failed run.
    /// </summary>
    public static ScenarioState StateOf(ScenarioJourney journey, string journeySignature, ScenarioRun? run)
    {
        ArgumentNullException.ThrowIfNull(journey);
        if (run is null) return ScenarioState.NeverRun;
        if (run.Signature is null || !string.Equals(run.Signature, journeySignature, StringComparison.OrdinalIgnoreCase)) return ScenarioState.Stale;

        // A journey with no parts proves nothing, so a run of it passes nothing.
        return journey.Parts.Count > 0 && journey.Parts.All(part => run.Part(part.Anchor)?.Outcome == ScenarioOutcome.Passed)
            ? ScenarioState.Passed
            : ScenarioState.Failed;
    }

    /// <summary>The state several journeys read as together — a scenarios file's row in
    /// the menu — in the dot's precedence: the first of never run, stale, failed that
    /// any of them is, and passed only when every one passed.</summary>
    public static ScenarioState Combined(IEnumerable<ScenarioState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var all = states.ToList();
        return all.Count == 0 ? ScenarioState.NeverRun : all.Min();
    }

    public static string Slug(ScenarioState state) => state switch
    {
        ScenarioState.Passed => "passed",
        ScenarioState.Failed => "failed",
        ScenarioState.Stale => "stale",
        _ => "never-run"
    };

    public static string Label(ScenarioState state) => state switch
    {
        ScenarioState.Passed => "Passed",
        ScenarioState.Failed => "Failed",
        ScenarioState.Stale => "Stale",
        _ => "Never run"
    };
}
