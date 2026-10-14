using System.Text.Json;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// One Copilot session's <c>events.jsonl</c>, reduced to the instants it records.
/// <para>
/// Folded a line at a time, for the reason <see cref="ClaudeTranscriptEvents"/>
/// gives. Copilot's files are the smaller of the two, but there are 691 of them on
/// this machine and the argument is the same. Nothing a line means here depends on
/// another, so the events are the whole of the state a resumed read carries.
/// </para>
/// </summary>
internal sealed class CopilotEventStream : IActivityEventParser
{
    private readonly List<ActivityEvent> _events;

    internal CopilotEventStream() => _events = [];

    private CopilotEventStream(CopilotEventStream other) => _events = [.. other._events];

    public IReadOnlyList<ActivityEvent> Events => _events;

    public IActivityEventParser Clone() => new CopilotEventStream(this);

    public void Observe(string line)
    {
        // EVERY LINE WITH A TIMESTAMP IS AN EVENT, AND THE TURN BRACKETS ARE NOT
        // USED.
        //
        // assistant.turn_start / assistant.turn_end look like exactly the authority
        // this class wants and they are not. 8 of 14,175 bracketed turns — 0.06% of
        // them — hold 58.5% of all bracketed time, and the longest is 41 HOURS,
        // because suspending and resuming a session leaves a turn open across the
        // whole gap. The genuine ones are median 6.7s, p90 16.7s, p99 80.2s.
        //
        // Gap inference over the raw event stream is immune to that: the 41-hour
        // turn is a 41-hour GAP between two events, which closes a run instead of
        // being one. Bracket pairing is not immune to it and cannot be made immune
        // to it. If you are here to "fix" this back to the brackets, re-measure
        // those two numbers first.
        //
        // And Copilot cannot mark a wait. user.message fires roughly zero seconds
        // after the event before it — 919 of them, every gap ~0 — so there is no
        // boundary in this format that means "the agent stopped and a person had
        // not yet answered". IsPrompt is therefore always false here, and the
        // surface says the waiting figure is Claude's alone rather than presenting
        // a half-measure as a whole one.
        if (line.Length == 0 || line[0] is not '{') return;

        try
        {
            using var document = JsonDocument.Parse(line);

            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) return;

            if (!root.TryGetProperty("timestamp", out var stamp)
                || stamp.ValueKind is not JsonValueKind.String
                || !stamp.TryGetDateTimeOffset(out var at))
            {
                return;
            }

            _events.Add(new ActivityEvent(at, IsPrompt: false));
        }
        catch (JsonException)
        {
            // One line, not the file, as the Claude fold answers it.
        }
    }
}
