using System.Globalization;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.UI.Components.Feedback;

namespace Backlog.Modules.Sessions.UI;

/// <summary>One line of a dispatcher's work list: a session request or an answer
/// delivery, as the loop card lists them.</summary>
/// <param name="What">What it is: the plan's tag, the entry, or the question to deliver.</param>
/// <param name="Kind"><c>plan</c>, <c>entry</c> or <c>delivery</c>.</param>
/// <param name="State">Where it stands, in a word.</param>
/// <param name="Tone">The dot beside it.</param>
/// <param name="Title">The full text behind <paramref name="What"/>, for its tooltip.</param>
public sealed record LoopWorkItem(string What, string Kind, string State, FlowStepTone Tone, string? Title = null);

/// <summary>
/// How a loop session reads on screen (local ADR 0051 §3, the design's "Loop session
/// states" artboard): its state as a chip, the line of what it holds, its wake reasons,
/// the four capacity tiles, and its work list. Pure functions, so the pane and its tests
/// say the same words.
/// </summary>
public static class LoopSessionView
{
    /// <summary>A loop state's word.</summary>
    public static string Label(LoopState state) => state switch
    {
        LoopState.Listening => "Listening",
        LoopState.Holding => "Holding",
        LoopState.Stalled => "Stalled",
        _ => "Not running"
    };

    /// <summary>The chip's tone slug on the badge tone scale
    /// (<c>.devbook/design/color-scheme.md#badge-and-chip-tones</c>): Listening is live,
    /// Holding quiet — it is waiting on purpose — Stalled an alert and Not running a fault.</summary>
    public static string Tone(LoopState state) => state switch
    {
        LoopState.Listening => "live",
        LoopState.Holding => "quiet",
        LoopState.Stalled => "alert",
        _ => "fault"
    };

    /// <summary>The chip a loop session's row shows before its first check-in.</summary>
    public const string NoCheckIn = "No check-in yet";

    /// <summary>What the person last asked of the loop, as the card says it, or null for nothing.</summary>
    public static string? CommandLine(LoopCommand command) => command switch
    {
        LoopCommand.Pause => "Paused: it does no work and keeps checking in",
        LoopCommand.Resume => "Resume asked",
        LoopCommand.Stop => "Stop asked: it ends its loop at its next check-in",
        _ => null
    };

    /// <summary>
    /// The row's line of what the loop holds. A dispatcher: the requests waiting for it,
    /// the sessions it started that still work against its limit, and the answers it holds
    /// to deliver. The Brain tender: the notes it has in hand. A pause leads the line.
    /// </summary>
    /// <param name="loop">The loop as its latest check-in left it.</param>
    /// <param name="waitingRequests">The session requests waiting in the queue; null where
    /// the host composed no queue, and then the line leaves them out.</param>
    /// <param name="limits">The limits its working sessions are held against.</param>
    /// <param name="measuredWorking">The dispatched sessions working as the app measured
    /// them (local ADR 0051 §4), so the row and the card's Sessions tile say one number;
    /// null where it measured none, and then the loop's own count stands.</param>
    public static string HeldLine(LoopSession loop, int? waitingRequests, CapacityLimits limits, int? measuredWorking = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(limits);

        var held = loop.CheckIn.Held;
        var working = measuredWorking ?? held.Sessions;
        var parts = new List<string>(4);

        if (loop.Command == LoopCommand.Pause) parts.Add("Paused");

        if (loop.CheckIn.Kind == LoopKind.BrainTender)
        {
            parts.Add(held.Notes == 0 ? "No notes in hand" : Count(held.Notes, "note", "notes") + " in hand");
        }
        else
        {
            if (waitingRequests is { } waiting) parts.Add(Count(waiting, "request", "requests") + " waiting");
            parts.Add($"{working.ToString(CultureInfo.InvariantCulture)} of {limits.Sessions.ToString(CultureInfo.InvariantCulture)} sessions working");
            parts.Add(Count(held.Deliveries, "answer", "answers") + " to deliver");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>When the loop wakes — what the card says under its tiles.</summary>
    public static string WakeLine(LoopCheckIn checkIn)
    {
        ArgumentNullException.ThrowIfNull(checkIn);

        var heartbeat = Heartbeat(checkIn.HeartbeatMinutes);

        return checkIn.Kind == LoopKind.BrainTender
            ? $"Wakes on the {heartbeat} heartbeat to file the inbox."
            : $"Wakes on a new request, a freed slot, an answer to deliver, or the {heartbeat} heartbeat.";
    }

    /// <summary>"30-minute", "2-hour", "1-day": a heartbeat as an adjective.</summary>
    public static string Heartbeat(int minutes) => minutes switch
    {
        _ when minutes % (24 * 60) == 0 => $"{(minutes / (24 * 60)).ToString(CultureInfo.InvariantCulture)}-day",
        _ when minutes % 60 == 0 => $"{(minutes / 60).ToString(CultureInfo.InvariantCulture)}-hour",
        _ => $"{minutes.ToString(CultureInfo.InvariantCulture)}-minute"
    };

    /// <summary>How long ago a check-in arrived, to the second while it is fresh:
    /// "40 s ago", "12 min ago", "3 h ago", "2 d ago".</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        return elapsed switch
        {
            { TotalMinutes: < 1 } => $"{(int)elapsed.TotalSeconds} s ago",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} min ago",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} h ago",
            _ => $"{(int)elapsed.TotalDays} d ago"
        };
    }

    /// <summary>One capacity tile: its label, its figure, its footnote, and whether its
    /// limit is reached. A null value is a figure this host could not read.</summary>
    public sealed record CapacityTile(string Label, string? Value, string Footnote, bool Held, string TestId);

    /// <summary>The four tiles, in the order sessions, plan usage, CPU, free memory.</summary>
    public static IReadOnlyList<CapacityTile> Tiles(CapacityReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var held = reading.HeldBy();
        var limits = reading.Limits;

        return
        [
            new(
                "Sessions",
                reading.WorkingSessions is { } working ? $"{working.ToString(CultureInfo.InvariantCulture)} / {limits.Sessions.ToString(CultureInfo.InvariantCulture)}" : null,
                held.Contains(CapacityLimit.Sessions) ? "limit reached" : "working now",
                held.Contains(CapacityLimit.Sessions),
                "loop-capacity-sessions"),
            new(
                "Plan usage",
                Percent(reading.PlanUsagePercent),
                Limit($"limit {Percent(limits.UsagePercent)}", held.Contains(CapacityLimit.Usage)),
                held.Contains(CapacityLimit.Usage),
                "loop-capacity-usage"),
            new(
                "CPU",
                Percent(reading.CpuPercent),
                Limit($"limit {Percent(limits.CpuPercent)}", held.Contains(CapacityLimit.Cpu)),
                held.Contains(CapacityLimit.Cpu),
                "loop-capacity-cpu"),
            new(
                "Free memory",
                reading.FreeMemoryGb is { } free ? $"{Math.Floor(free).ToString(CultureInfo.InvariantCulture)} GB" : null,
                Limit($"needs {limits.MinFreeGb.ToString(CultureInfo.InvariantCulture)} GB", held.Contains(CapacityLimit.Memory)),
                held.Contains(CapacityLimit.Memory),
                "loop-capacity-memory")
        ];
    }

    /// <summary>A tile's footnote, naming the limit as the one that holds the loop when it
    /// is reached — in words, so the held tile never rests on its edge alone.</summary>
    private static string Limit(string limit, bool reached) => reached ? $"{limit} · reached" : limit;

    /// <summary>
    /// A dispatcher's work list: the session requests still open — Requested, Claimed or
    /// Started, a lapsed claim read as Requested — oldest first, then the answer deliveries
    /// still to make. A request carries none of its entry's text (local ADR 0038), so an
    /// entry is named by its id and a plan by its tag.
    /// </summary>
    public static IReadOnlyList<LoopWorkItem> Work(
        IEnumerable<SessionRequest> requests,
        IEnumerable<WaitingQuestion> undelivered,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(undelivered);

        var items = new List<LoopWorkItem>();

        foreach (var request in requests.Where(request => request.IsOpenAt(now)).OrderBy(request => request.CreatedAt))
        {
            var state = request.StateAt(now);
            var (what, title) = request.Kind == SessionRequestKind.Plan
                ? ($"+{SessionTitles.Bare(request.PlanTag ?? string.Empty)}", $"Plan +{SessionTitles.Bare(request.PlanTag ?? string.Empty)} in {request.Repository}")
                : ($"Entry {Short(request.EntryId)}", $"Entry {request.EntryId} in {request.Repository}");

            items.Add(new LoopWorkItem(
                what,
                request.Kind == SessionRequestKind.Plan ? "plan" : "entry",
                state.ToString(),
                state switch
                {
                    SessionRequestState.Started => FlowStepTone.Done,
                    SessionRequestState.Claimed => FlowStepTone.Active,
                    _ => FlowStepTone.Pending
                },
                title));
        }

        foreach (var question in undelivered.OrderBy(question => question.AskedAt))
        {
            var delivery = question.DeliveryAt(now);

            if (delivery is not (AnswerDeliveryState.Pending or AnswerDeliveryState.Claimed)) continue;

            var text = question.Questions.Count > 0 ? question.Questions[0].Question : "An answer";

            items.Add(new LoopWorkItem(
                text,
                "delivery",
                delivery == AnswerDeliveryState.Claimed ? "Delivering" : "To deliver",
                FlowStepTone.Active,
                $"Answer for session {question.SessionId}: {text}"));
        }

        return items;
    }

    private static string Short(Guid? id) => id is { } value ? value.ToString("N")[..8] : "—";

    private static string? Percent(double? value) =>
        value is { } percent ? $"{Math.Round(percent).ToString(CultureInfo.InvariantCulture)}%" : null;

    private static string Count(int count, string one, string many) =>
        count == 1 ? $"1 {one}" : $"{count.ToString(CultureInfo.InvariantCulture)} {many}";
}
