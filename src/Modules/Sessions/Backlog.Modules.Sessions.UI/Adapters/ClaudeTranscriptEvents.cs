using System.Text.Json;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// One Claude transcript, reduced to the instants it records and which of them a
/// person caused.
/// <para>
/// Streamed a line at a time and never held. A week of Claude on this machine is
/// 309 MB across 120,419 lines, and reading a transcript into a string to split it
/// would put that on the heap of a desktop app to produce a few hundred intervals.
/// </para>
/// </summary>
internal static class ClaudeTranscriptEvents
{
    internal static async Task<IReadOnlyList<ActivityEvent>> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var events = new List<ActivityEvent>();

        using var reader = new StreamReader(path);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            // Four rules, all four measured against this machine's own 120,419 lines.
            // Every one of them is a rule somebody will otherwise "simplify" back into a
            // bug, and rule 1 already was one once.
            //
            // 1. ONLY A CONVERSATION TURN IS AN EVENT — `user`, `assistant`,
            //    `attachment`, and nothing else. This is the rule that looks like an
            //    arbitrary narrowing and is not, so here is what it costs to drop:
            //    admitting every timestamped line instead takes the waiting figure over a
            //    week from 105.8 hours to ZERO, and inflates agent-active from 90.5 to
            //    100.8. Not skewed — annihilated.
            //
            //    The mechanism is that the other timestamped types are bookkeeping that
            //    lands *between* turns: queue-operation (6,856 lines in a week), pr-link,
            //    frame-link, file-history-delta, system. A wait is a gap of more than
            //    IdleAfter that a prompt ended, so a single queue-operation sitting
            //    inside that gap splits it into two short ones, no gap is left to end in
            //    a prompt, and the silence it was measuring is booked as work instead.
            //    Every wait on this machine died that way, all 103 of them.
            //
            //    So the test is what an event says about *the agent*, not whether the
            //    line happens to carry a clock.
            //
            // 2. NO TIMESTAMP, NO EVENT. 23,219 lines — 19% — carry no `timestamp` at
            //    all: last-prompt, atis-latch, custom-title, mode, bridge-session,
            //    permission-mode, ai-title, agent-name. Every one of them is UI or
            //    session state rather than anything the agent did, and dating them off
            //    the file would be inventing the activity this whole class exists to
            //    stop inventing.
            //
            // 3. A PROMPT IS `promptSource` BEING PRESENT. Not absent, not a value —
            //    presence. Values seen: sdk (768), typed (41), system (37). Keying on
            //    the field's presence is what makes the rule survive a fourth value.
            //
            // 4. ABSENT DOES NOT MEAN TOOL RESULT. It usually does — 22,213 of them —
            //    but 259 lines with no promptSource carry text content: skill
            //    injections (isMeta), "[Request interrupted by user]", and
            //    slash-command plumbing. A conversation turn with a timestamp and no
            //    promptSource is simply activity; do not try to name it.
            if (line.Length == 0 || line[0] is not '{') continue;

            try
            {
                using var document = JsonDocument.Parse(line);

                var root = document.RootElement;

                if (root.ValueKind is not JsonValueKind.Object) continue;

                if (!root.TryGetProperty("type", out var kind)
                    || kind.ValueKind is not JsonValueKind.String
                    || !IsTurn(kind.GetString()))
                {
                    continue;
                }

                if (!root.TryGetProperty("timestamp", out var stamp)
                    || stamp.ValueKind is not JsonValueKind.String
                    || !stamp.TryGetDateTimeOffset(out var at))
                {
                    continue;
                }

                events.Add(new ActivityEvent(at, root.TryGetProperty("promptSource", out _)));
            }
            catch (JsonException)
            {
                // A line being appended as this reads it, or a shape this version does
                // not know. One line, not the file: the same proportionate answer the
                // session readers give a half-written live file.
                continue;
            }
        }

        return events;
    }

    /// <summary>
    /// Whether a line is a turn in the conversation rather than something written beside
    /// it. See rule 1 above for what admitting the rest costs.
    /// <para>
    /// A closed list rather than a list of things to exclude, because the two fail in
    /// opposite directions: a turn type this does not know yet is left out, which
    /// understates a figure the surface already says is a floor, while a bookkeeping type
    /// nobody thought of is let in, which silently deletes waits. Of the two, the one
    /// that goes quiet is the one to avoid.
    /// </para>
    /// </summary>
    private static bool IsTurn(string? type) =>
        type is "user" or "assistant" or "attachment";
}
