using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Feedback;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// A whole plan on My Day (local ADR 0062 §8; <c>.devbook/domain/tasks/features.md#my-day</c>).
/// <para>
/// The date lives on the plan's Roadmap Items in Roadmap Planning and rides the roadmap
/// plan document on the task feed (ADR 0018); this state only reads it, through its own
/// port (<see cref="IRoadmapTagSource.PlansOnMyDayAsync"/>), and reads it by exactly the
/// rule a task's <c>myday:</c> stamp is read by (<see cref="MyDay.Includes"/>) under the
/// same Clear My Day overnight switch. A plan counts as finished, for that rule, once every
/// entry it gathers is ticked off.
/// </para>
/// </summary>
public sealed partial class TasksDesktopState
{
    /// <summary>The plans put on My Day and the day each was picked for, by the roadmap's
    /// slug of the plan tag (<see cref="PlanKey"/>). Read with <see cref="RoadmapTags"/>, so
    /// a plan change and a reload refresh both.</summary>
    public IReadOnlyDictionary<string, DateOnly> PlansOnMyDay { get; private set; } =
        new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The day the plan <paramref name="plan"/> — <c>+</c> optional — was put on
    /// My Day, or null while it is on none.</summary>
    public DateOnly? PlanMyDayOn(string plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return PlansOnMyDay.TryGetValue(PlanKey(plan), out var day) ? day : null;
    }

    /// <summary>
    /// A plan tag as Roadmap Planning keys its items: the bare tag lower-cased, every run of
    /// anything not a letter or digit one hyphen, the ends trimmed — so <c>+Lets_BBQ</c> and
    /// the item tagged <c>lets-bbq</c> it creates are one plan here too.
    /// </summary>
    internal static string PlanKey(string? plan)
    {
        var bare = PlanQueueOffer.Bare(plan).ToLowerInvariant();
        var key = new System.Text.StringBuilder(bare.Length);
        var hyphen = false;

        foreach (var character in bare)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (hyphen && key.Length > 0) key.Append('-');
                key.Append(character);
                hyphen = false;
            }
            else
            {
                hyphen = true;
            }
        }

        return key.ToString();
    }

    /// <summary>Whether the plan still has something to do: an open entry, or no entry yet.</summary>
    public bool IsPlanOpen(string plan) => PlanFinishedOn(plan) is null;

    /// <summary>
    /// Whether the plan <paramref name="plan"/> is in the My Day of <paramref name="today"/>:
    /// the task rule over the plan's stamp, with the plan finished on the day its last entry
    /// was ticked off. So with Clear My Day overnight on, the plan is in only while its day
    /// is today; with it off, a plan picked earlier carries over until it is finished.
    /// </summary>
    public bool IsPlanInMyDay(string plan, DateOnly today) =>
        PlanMyDayOn(plan) is { } stamp
        && MyDay.Includes(stamp, PlanFinishedOn(plan), today, _myDayPreferences.ClearsOvernight);

    /// <summary>The plans in today's My Day, <c>+</c> and all, in the order the roadmap
    /// lists its tags, then any other stamped plan in name order.</summary>
    public IReadOnlyList<string> PlansInMyDay(DateOnly today)
    {
        // Only a stamped plan can be in, so the roadmap's order is read over those alone.
        if (PlansOnMyDay.Count == 0) return [];

        var ordered = RoadmapTags
            .Select(PlanKey)
            .Where(PlansOnMyDay.ContainsKey)
            .Concat(PlansOnMyDay.Keys.Order(StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. ordered.Where(key => IsPlanInMyDay(key, today)).Select(key => "+" + key)];
    }

    /// <summary>
    /// Puts the plan <paramref name="plan"/> on the My Day of <paramref name="on"/>, or takes
    /// it off with null. A plan tag no Roadmap Item carries gets its item, in Roadmap
    /// Planning. Taken here at once when the plan stored it, so the sun answers the press
    /// before the plan's change notice has been read back.
    /// </summary>
    /// <returns>Whether the plan stored it.</returns>
    public async Task<bool> SetPlanMyDayAsync(string plan, DateOnly? on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);

        var bare = PlanKey(plan);
        if (bare.Length == 0) return false;

        bool stored;
        try
        {
            stored = await _roadmapTags.SetPlanMyDayAsync(bare, on, _untilDisposed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stored = false;
        }

        if (!stored)
        {
            _toasts?.Publish(ToastMessage.Error($"+{bare} could not be {(on is null ? "taken off" : "put on")} My Day."));
            return false;
        }

        var stamps = new Dictionary<string, DateOnly>(PlansOnMyDay, StringComparer.OrdinalIgnoreCase);
        if (on is { } day) stamps[bare] = day;
        else stamps.Remove(bare);
        PlansOnMyDay = stamps;

        // A tag the roadmap did not carry has an item now: offered and queued as a plan.
        if (on is not null && !RoadmapTags.Any(tag => string.Equals(PlanKey(tag), bare, StringComparison.Ordinal)))
        {
            RoadmapTags = [.. RoadmapTags, "+" + bare];
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Puts the plan on today's My Day, or takes it off when it is in it — the
    /// sun on the plan's heading, its waves view and its Plans today line.</summary>
    public Task<bool> TogglePlanMyDayAsync(string plan, DateOnly today) =>
        SetPlanMyDayAsync(plan, IsPlanInMyDay(plan, today) ? null : today);

    /// <summary>
    /// What Plans today lists under a plan (local ADR 0062 §8): only its open entries that
    /// are the person's — left for you, held for you, or awaiting validation on an open pull
    /// request — in the store's order.
    /// </summary>
    /// <param name="plan">The plan tag, <c>+</c> optional.</param>
    /// <param name="run">The plan's run, for the entries' states.</param>
    public IReadOnlyList<(EntryRow Row, PlanEntryState State)> PlanEntriesForYou(string plan, PlanRun? run = null) =>
        [.. PlanEntryStatesOf(plan, run).Where(pair => IsForYou(pair.Row, pair.State))];

    /// <summary>Whether a plan entry waits on the person rather than the run.</summary>
    internal static bool IsForYou(EntryRow row, PlanEntryState state)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(state);

        return state.State switch
        {
            PlanEntryQueueState.LeftForYou or PlanEntryQueueState.HeldForYou => true,
            PlanEntryQueueState.Done or PlanEntryQueueState.Landed => false,
            _ => IsAwaitingValidation(row),
        };
    }

    /// <summary>Whether an entry's work waits on the person's validation: a pull request it
    /// links to is open, a draft or ready for review.</summary>
    internal static bool IsAwaitingValidation(EntryRow row) =>
        row.PullRequestStatuses.Values.Any(pullRequest =>
            pullRequest.State is LinkedPullRequestState.Draft or LinkedPullRequestState.Open);

    /// <summary>The day the plan was finished — its last entry ticked off, once every entry
    /// it gathers is — or null while any is open or it gathers none.</summary>
    private DateOnly? PlanFinishedOn(string plan)
    {
        var bare = PlanKey(plan);
        DateOnly? last = null;
        var any = false;

        foreach (var row in Rows)
        {
            if (PlanEntryState.PlanTagOf(row) is not { } tag
                || !string.Equals(PlanKey(tag), bare, StringComparison.Ordinal))
            {
                continue;
            }

            if (row.PreviewCompletedOn is not { } finished) return null;

            any = true;
            if (last is null || finished > last) last = finished;
        }

        return any ? last : null;
    }
}
