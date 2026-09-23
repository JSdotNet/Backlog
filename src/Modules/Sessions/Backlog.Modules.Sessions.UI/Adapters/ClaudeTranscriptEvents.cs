using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

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
            //
            // 5. A REFUSAL IS `error` BEING "rate_limit". Not isApiErrorMessage, which
            //    a parse failure and "No response requested" carry too; not the text,
            //    which is prose and has already been reworded once ("session limit",
            //    "individual spend limit"). The 82 refusals on this machine all carry
            //    the field with that value and a 429 beside it. Which limit is
            //    `quotaLimits.rateLimitType`, kept raw beside the kind it maps to.
            //    The line is still a turn and still an event — rule 1 admitted it
            //    before it had a name, and giving it one must not move a figure.
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

                events.Add(new ActivityEvent(at, root.TryGetProperty("promptSource", out _), LimitOf(root, at)));
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

    /// <summary>
    /// The refusal a line records, or null for the overwhelming majority that record
    /// none. See rule 5 above for what marks one.
    /// </summary>
    private static AgentLimitHit? LimitOf(JsonElement root, DateTimeOffset at)
    {
        if (!root.TryGetProperty("error", out var error)
            || error.ValueKind is not JsonValueKind.String
            || error.GetString() is not "rate_limit")
        {
            return null;
        }

        // The bucket is nested and optional: 2.1.229 wrote refusals with no quotaLimits
        // block at all — 37 of the 119 on this machine. Those name the limit only in
        // their prose, so the prose is read when the field is absent and never when it
        // is present: the field is what Claude Code itself keys on, and a spend-limit
        // refusal's text says "session limit resets" under a seven_day field. The raw
        // type stays null either way the field is missing, because the transcript
        // named no bucket and the record must not say it did.
        var type = root.TryGetProperty("quotaLimits", out var quota)
            && quota.ValueKind is JsonValueKind.Object
            && quota.TryGetProperty("rateLimitType", out var name)
            && name.ValueKind is JsonValueKind.String
                ? name.GetString()
                : null;

        var block = root.TryGetProperty("quotaLimits", out var limits) && limits.ValueKind is JsonValueKind.Object
            ? limits
            : (JsonElement?)null;

        // The reset instant and the overage half, from the same optional block. Absent
        // or malformed reads as null rather than as a value nothing wrote down.
        return new AgentLimitHit(at, type is null ? KindOf(TextOf(root)) : KindOf(type), type)
        {
            ResetsAt = InstantOf(block, "resetsAt"),
            OverageStatus = StringOf(block, "overageStatus"),
            OverageResetsAt = InstantOf(block, "overageResetsAt"),
            OverageDisabledReason = StringOf(block, "overageDisabledReason"),
            IsUsingOverage = block is { } held
                && held.TryGetProperty("isUsingOverage", out var overage)
                && overage.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? overage.GetBoolean()
                    : null
        };
    }

    /// <summary>A Unix-seconds field of the quota block as an instant, or null.</summary>
    private static DateTimeOffset? InstantOf(JsonElement? block, string name) =>
        block is { } held
        && held.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.Number
        && value.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>A string field of the quota block, or null.</summary>
    private static string? StringOf(JsonElement? block, string name) =>
        block is { } held
        && held.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The refusal's own sentence — the first text block of the message, or
    /// null where there is none.</summary>
    private static string? TextOf(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || message.ValueKind is not JsonValueKind.Object
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind is JsonValueKind.Object
                && block.TryGetProperty("text", out var text)
                && text.ValueKind is JsonValueKind.String)
            {
                return text.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Claude Code's own names for the buckets, from its own code: it labels
    /// <c>five_hour</c> "session limit", <c>seven_day</c> "weekly limit" and
    /// <c>seven_day_overage_included</c> "Fable limit", which is what the plan's usage
    /// page shows as 5-hour, Weekly · all models and Weekly · Fable. Its vocabulary
    /// also holds <c>seven_day_opus</c>, <c>seven_day_sonnet</c> and <c>overage</c>;
    /// those are deliberately Other, because the model this maps onto is not per
    /// model — see <see cref="AgentLimitKind"/>.
    /// <para>
    /// Two phrases from the prose beside the three names from the field, for the
    /// refusals that carry no field. "session limit" and "weekly limit" are the two
    /// 2.1.229 wrote; nothing on any profile yet shows what an older Fable refusal
    /// would have said, so there is no phrase for it here and one would be a guess.
    /// </para>
    /// </summary>
    private static AgentLimitKind KindOf(string? rateLimitTypeOrText) =>
        rateLimitTypeOrText switch
        {
            "five_hour" => AgentLimitKind.FiveHour,
            "seven_day" => AgentLimitKind.Weekly,
            "seven_day_overage_included" => AgentLimitKind.WeeklyFable,
            { } text when text.Contains("session limit", StringComparison.Ordinal) => AgentLimitKind.FiveHour,
            { } text when text.Contains("weekly limit", StringComparison.Ordinal) => AgentLimitKind.Weekly,
            _ => AgentLimitKind.Other
        };
}
