using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// What a session is for, as its title says — the prefix a person writes by hand or
/// the shape a backlog-tools skill writes. Declared in the order a grouping by
/// purpose draws its sections and the Purpose filter offers its options.
/// <para>
/// Not called a kind: <see cref="AgentSessionKind"/> is the agent, and a session is
/// a Claude session and an Artifact at once.
/// </para>
/// </summary>
public enum SessionPurpose
{
    /// <summary><c>ARTIFACT: title</c> — a session kept for something it produced.</summary>
    Artifact,

    /// <summary><c>DESIGN: title</c>.</summary>
    Design,

    /// <summary><c>PLAN: +tag — title</c> or <c>+PLAN: …</c> — the session a plan was agreed in.</summary>
    Plan,

    /// <summary><c>RESEARCH: title</c>.</summary>
    Research,

    /// <summary><c>HOLD: title</c> — set aside, to come back to.</summary>
    Hold,

    /// <summary><c>+tag:execute-plan [@repo]</c> — the title execute-plan gives its session.</summary>
    PlanRun,

    /// <summary><c>tag:n - title</c> — the title run-plan-item gives a numbered entry's session.</summary>
    Entry,

    /// <summary>A scheduled task's run or a schedule's row, whatever its title says.</summary>
    Scheduled,

    /// <summary>Any other title.</summary>
    Other
}

/// <summary>Whether a plan has been brought into the Backlog app, as its PLAN
/// session's title says: <c>+PLAN:</c> is Imported, <c>PLAN:</c> is Draft.</summary>
public enum PlanState
{
    Draft,
    Imported
}

/// <summary>
/// One title, read.
/// </summary>
/// <param name="Purpose">What the session is for.</param>
/// <param name="PlanTag">The plan the session belongs to, without its <c>+</c>, as the
/// title spells it; null where it names none.</param>
/// <param name="State">The state the title itself says, on a Plan session only — the
/// plan's own state is <see cref="SessionPlans.Of"/>'s.</param>
/// <param name="Step">The entry's step number, on an Entry only.</param>
/// <param name="Shown">The title less its prefix — what the row shows.</param>
/// <param name="Raw">The whole title — what the row's tooltip and every other
/// surface keep.</param>
public sealed record SessionTitleReading(
    SessionPurpose Purpose,
    string? PlanTag,
    PlanState? State,
    int? Step,
    string Shown,
    string Raw);

/// <summary>
/// Reading a session's title for its purpose and plan. A pure function over the
/// title it is given; see <c>.devbook/domain/sessions/domain.md#session-title-reading</c>
/// for the grammar.
/// <para>
/// Prefixes are uppercase only, so a sentence that happens to open "Plan: …" stays
/// Other. The skill-written shapes are the ones
/// <c>plugins/backlog-tools/skills/execute-plan/assets/handoff.md</c> and
/// <c>import-plan/assets/backlog-import-grammar.md#step-numbers</c> own; an unnumbered
/// <c>tag - title</c> is indistinguishable from an ordinary title and is not read.
/// </para>
/// </summary>
public static class SessionTitles
{
    private static readonly (string Prefix, SessionPurpose Purpose, PlanState? State)[] Prefixes =
    [
        ("ARTIFACT:", SessionPurpose.Artifact, null),
        ("DESIGN:", SessionPurpose.Design, null),
        ("+PLAN:", SessionPurpose.Plan, PlanState.Imported),
        ("PLAN:", SessionPurpose.Plan, PlanState.Draft),
        ("RESEARCH:", SessionPurpose.Research, null),
        ("HOLD:", SessionPurpose.Hold, null)
    ];

    // "+tag — title", with an em dash, an en dash, or a spaced hyphen; the hyphen needs
    // its spaces because a tag may hold one.
    private static readonly Regex PlanTagLead = new(
        @"^\+(?<tag>[^\s:—–]+)(?:\s*[—–]\s*|\s+-\s+|\s*$)(?<title>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex PlanRunTitle = new(
        @"^\+?(?<tag>[^\s:]+):(?<rest>execute-plan(?:\s.*)?)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex EntryTitle = new(
        @"^(?<tag>[^\s:+][^\s:]*):(?<rest>(?<step>[0-9]+)\s+-\s+.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    // Keyed on the row instance, not on record equality: the reading is worked out once
    // per row a reading produced, however often the pane groups and filters it.
    private static readonly ConditionalWeakTable<SessionRow, SessionTitleReading> Readings = new();

    /// <summary>Reads one title.</summary>
    public static SessionTitleReading Read(string? title)
    {
        var raw = title ?? string.Empty;
        var text = raw.Trim();

        foreach (var (prefix, purpose, state) in Prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var rest = text[prefix.Length..].Trim();

            if (purpose is SessionPurpose.Plan
                && PlanTagLead.Match(rest) is { Success: true } lead
                && SessionRequests.IsPlanTag(lead.Groups["tag"].Value))
            {
                return new(purpose, lead.Groups["tag"].Value, state, null, OrRaw(lead.Groups["title"].Value.Trim(), raw), raw);
            }

            return new(purpose, null, state, null, OrRaw(rest, raw), raw);
        }

        if (PlanRunTitle.Match(text) is { Success: true } run && SessionRequests.IsPlanTag(run.Groups["tag"].Value))
        {
            return new(SessionPurpose.PlanRun, run.Groups["tag"].Value, null, null, run.Groups["rest"].Value.Trim(), raw);
        }

        if (EntryTitle.Match(text) is { Success: true } entry
            && SessionRequests.IsPlanTag(entry.Groups["tag"].Value)
            && int.TryParse(entry.Groups["step"].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var step))
        {
            return new(SessionPurpose.Entry, entry.Groups["tag"].Value, null, step, entry.Groups["rest"].Value.Trim(), raw);
        }

        return new(SessionPurpose.Other, null, null, null, raw, raw);
    }

    /// <summary>
    /// Reads a row: a scheduled task's run (<see cref="AgentSession.ScheduledTask"/>) or
    /// a schedule's row (<see cref="SessionRow.Schedule"/>) is Scheduled whatever its
    /// title says; any other row is its <see cref="SessionRow.Title"/>, read.
    /// </summary>
    public static SessionTitleReading Of(SessionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return Readings.GetValue(row, static row =>
            !string.IsNullOrWhiteSpace(row.Session?.ScheduledTask) || row.Schedule is not null
                ? new(SessionPurpose.Scheduled, null, null, null, row.Title, row.Title)
                : Read(row.Title));
    }

    /// <summary>Whether two tags name one plan: case-blind, with one leading <c>+</c>
    /// lifted from either.</summary>
    public static bool SameTag(string? left, string? right) =>
        left is not null && right is not null
        && string.Equals(Bare(left), Bare(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>A tag without its surrounding space and one leading <c>+</c>.</summary>
    public static string Bare(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var trimmed = tag.Trim();

        return trimmed.StartsWith('+') ? trimmed[1..] : trimmed;
    }

    /// <summary>Whether the purpose is one a session keeps until it is archived —
    /// Artifact, Design, Plan, Research, Hold — which When draws under Standing.</summary>
    public static bool IsStanding(SessionPurpose purpose) =>
        purpose is SessionPurpose.Artifact or SessionPurpose.Design or SessionPurpose.Plan
            or SessionPurpose.Research or SessionPurpose.Hold;

    /// <summary>What a purpose is called on screen: its badge, its filter option and
    /// its section heading are one word.</summary>
    public static string Label(SessionPurpose purpose) => purpose switch
    {
        SessionPurpose.PlanRun => "Plan run",
        _ => purpose.ToString()
    };

    /// <summary>What a plan state is called on screen.</summary>
    public static string Label(PlanState state) => state.ToString();

    private static string OrRaw(string shown, string raw) => shown.Length > 0 ? shown : raw;
}

/// <summary>Narrowing rows to one purpose, and counting each. Composed like
/// <see cref="AgentSessionEnvironments"/>: narrow first, then group.</summary>
public static class SessionPurposes
{
    /// <summary>The rows of one purpose, in the order given, or the same list when
    /// none is named.</summary>
    public static IReadOnlyList<SessionRow> On(IReadOnlyList<SessionRow> rows, SessionPurpose? purpose)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (purpose is not { } wanted) return rows;

        return [.. rows.Where(row => SessionTitles.Of(row).Purpose == wanted)];
    }

    /// <summary>How many of these rows each purpose holds, every purpose present.</summary>
    public static IReadOnlyDictionary<SessionPurpose, int> Counts(IReadOnlyList<SessionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = Enum.GetValues<SessionPurpose>().ToDictionary(purpose => purpose, _ => 0);

        foreach (var row in rows)
        {
            counts[SessionTitles.Of(row).Purpose]++;
        }

        return counts;
    }
}

/// <summary>One plan a reading names, as a filter offers it.</summary>
/// <param name="Tag">The bare tag, as its most recent row spells it.</param>
/// <param name="State">Imported when the plan has a <c>+PLAN</c> session or any plan
/// run — execute-plan runs only imported plans; Draft when it has only <c>PLAN:</c>
/// sessions; null when it has neither.</param>
public sealed record SessionPlan(string Tag, PlanState? State);

/// <summary>The plans a reading names, and narrowing rows to one plan or one state.</summary>
public static class SessionPlans
{
    /// <summary>Every tag these rows carry, case-blind, ordered by tag, each with
    /// its plan's state.</summary>
    public static IReadOnlyList<SessionPlan> Of(IReadOnlyList<SessionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return
        [
            .. rows
                .OrderByDescending(row => row.LastActivityAt)
                .Select(SessionTitles.Of)
                .Where(reading => reading.PlanTag is not null)
                .GroupBy(reading => reading.PlanTag!, StringComparer.OrdinalIgnoreCase)
                .Select(plan => new SessionPlan(plan.First().PlanTag!, StateOf(plan)))
                .OrderBy(plan => plan.Tag, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>The rows of one plan, in the order given, or the same list when no
    /// tag is named. Case-blind, with a leading <c>+</c> lifted.</summary>
    public static IReadOnlyList<SessionRow> On(IReadOnlyList<SessionRow> rows, string? tag)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (string.IsNullOrWhiteSpace(tag)) return rows;

        return [.. rows.Where(row => SessionTitles.SameTag(SessionTitles.Of(row).PlanTag, tag))];
    }

    /// <summary>
    /// Every row of a plan in this state, in the order given, or the same list when no
    /// state is named. A tagged row takes its plan's state from
    /// <paramref name="plans"/> — the whole reading's, so a narrowing applied first
    /// never changes a plan's state — and a Plan session with no tag its own.
    /// </summary>
    public static IReadOnlyList<SessionRow> InState(
        IReadOnlyList<SessionRow> rows,
        PlanState? state,
        IReadOnlyList<SessionPlan>? plans = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (state is not { } wanted) return rows;

        var states = (plans ?? Of(rows))
            .GroupBy(plan => plan.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(plan => plan.Key, plan => plan.First().State, StringComparer.OrdinalIgnoreCase);

        PlanState? StateOfRow(SessionTitleReading reading) => reading.PlanTag is { } tag
            ? states.GetValueOrDefault(tag)
            : reading.Purpose is SessionPurpose.Plan ? reading.State : null;

        return [.. rows.Where(row => StateOfRow(SessionTitles.Of(row)) == wanted)];
    }

    private static PlanState? StateOf(IEnumerable<SessionTitleReading> readings)
    {
        var all = readings.ToList();

        if (all.Any(reading => reading.Purpose is SessionPurpose.PlanRun
                || reading is { Purpose: SessionPurpose.Plan, State: PlanState.Imported }))
        {
            return PlanState.Imported;
        }

        return all.Any(reading => reading.Purpose is SessionPurpose.Plan) ? PlanState.Draft : null;
    }
}
