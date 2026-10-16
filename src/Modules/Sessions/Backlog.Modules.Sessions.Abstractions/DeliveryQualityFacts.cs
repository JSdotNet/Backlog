using System.Text.Json;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// A run's cumulative Quality Facts, as the delivery contract defines them: the review
/// rounds it took, the blockers each round left open, and the defects found later than
/// review. The flow-runner keeps them in <c>runContext.quality</c> while the run is under
/// way and sends them on <c>finish_run</c> as the run's totals; this is what either one
/// said, whichever the run file holds — <c>finish_run</c>'s first.
/// <para>
/// The run's record, where a stage's <see cref="DeliveryRunStage.Round"/>,
/// <see cref="DeliveryRunStage.Blockers"/> and <see cref="DeliveryRunStage.DefectsFound"/>
/// describe only that stage's latest pass. A review that ends on a clean round reports
/// zero blockers on its stage, and a Verify that found a defect and re-ran clean reports
/// zero defects; the facts keep both.
/// </para>
/// <para>
/// Scalars only, so two reads of one file are equal. Every value is null where the run
/// did not say it: a run with no implement ⇄ review loop sends no rounds or blockers,
/// and absent is never read as zero.
/// </para>
/// </summary>
/// <param name="ReviewRounds">Every Review pass over every slice, fix rounds included —
/// <c>reviewRounds.total</c>, or the sum of its <c>slices</c> where it states no total.</param>
/// <param name="HighestSliceRounds">The most rounds any one slice took: the rounds a
/// run with one slice took, and the figure a stage's own <c>round</c> counts.</param>
/// <param name="BlockerRounds">How many rounds <c>blockers.rounds</c> lists.</param>
/// <param name="BlockersOpen">The blockers those rounds listed open, summed over them —
/// a blocker left open across two rounds counts in each, as the contract counts it.</param>
/// <param name="EscapedDefects">Every defect found later than review, at any stage —
/// <c>escapedDefects.total</c>, or its items counted.</param>
/// <param name="VerifyDefects">The escaped defects Verify recorded first, counted from
/// <c>escapedDefects.items</c> by <c>foundAt</c>; null where the run lists no items.</param>
public sealed record DeliveryQualityFacts(
    int? ReviewRounds,
    int? HighestSliceRounds,
    int? BlockerRounds,
    int? BlockersOpen,
    int? EscapedDefects,
    int? VerifyDefects)
{
    /// <summary>The mean blockers a round left open, or null where the run listed no
    /// round.</summary>
    public double? BlockersPerRound => BlockerRounds is > 0 and var rounds && BlockersOpen is { } open
        ? (double)open / rounds
        : null;

    /// <summary>
    /// The facts the run file states: its <c>quality</c> object, where <c>finish_run</c>
    /// sent one, else the one its <c>runContext</c> keeps. Null where neither holds a
    /// fact this reads.
    /// </summary>
    public static DeliveryQualityFacts? Of(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object) return null;

        if (root.TryGetProperty("quality", out var finished) && Read(finished) is { } closed) return closed;

        return root.TryGetProperty("runContext", out var context)
               && context.ValueKind is JsonValueKind.Object
               && context.TryGetProperty("quality", out var running)
            ? Read(running)
            : null;
    }

    /// <summary>One <c>quality</c> object read, or null where it is not an object or
    /// carries none of the three facts.</summary>
    public static DeliveryQualityFacts? Read(JsonElement quality)
    {
        if (quality.ValueKind is not JsonValueKind.Object) return null;

        var (rounds, highest) = Rounds(quality);
        var (blockerRounds, open) = Blockers(quality);
        var (escaped, verify) = Escaped(quality);

        if (rounds is null && highest is null && blockerRounds is null && escaped is null && verify is null) return null;

        return new DeliveryQualityFacts(rounds, highest, blockerRounds, open, escaped, verify);
    }

    /// <summary>
    /// Whether a stage name is Verify's — in any casing, the way a phase is matched
    /// everywhere else (<see cref="DeliveryEconomics.MeasureOf"/>).
    /// </summary>
    private static bool IsVerify(string? stage) =>
        stage is not null && string.Equals(new string([.. stage.Where(char.IsLetter)]), "verify", StringComparison.OrdinalIgnoreCase);

    private static (int? Total, int? Highest) Rounds(JsonElement quality)
    {
        if (!quality.TryGetProperty("reviewRounds", out var rounds) || rounds.ValueKind is not JsonValueKind.Object) return (null, null);

        var slices = rounds.TryGetProperty("slices", out var list) && list.ValueKind is JsonValueKind.Array
            ? list.EnumerateArray().Select(slice => Count(slice, "rounds")).OfType<int>().ToList()
            : [];

        var total = Count(rounds, "total") ?? (slices.Count > 0 ? slices.Sum() : null);
        var highest = slices.Count > 0 ? slices.Max() : total;

        return (total, highest);
    }

    private static (int? Rounds, int? Open) Blockers(JsonElement quality)
    {
        if (!quality.TryGetProperty("blockers", out var blockers)
            || blockers.ValueKind is not JsonValueKind.Object
            || !blockers.TryGetProperty("rounds", out var rounds)
            || rounds.ValueKind is not JsonValueKind.Array)
        {
            return (null, null);
        }

        var listed = rounds.EnumerateArray().Where(round => round.ValueKind is JsonValueKind.Object).ToList();

        if (listed.Count == 0) return (null, null);

        var open = listed.Sum(round => Open(round, "rule") + Open(round, "bug"));

        return (listed.Count, open);
    }

    private static (int? Total, int? Verify) Escaped(JsonElement quality)
    {
        if (!quality.TryGetProperty("escapedDefects", out var escaped) || escaped.ValueKind is not JsonValueKind.Object) return (null, null);

        var items = escaped.TryGetProperty("items", out var list) && list.ValueKind is JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind is JsonValueKind.Object).ToList()
            : null;

        var total = Count(escaped, "total") ?? items?.Count;
        var verify = items?.Count(item => IsVerify(item.TryGetProperty("foundAt", out var at) && at.ValueKind is JsonValueKind.String ? at.GetString() : null));

        // A total of none says Verify found none too, items or not.
        if (verify is null && total == 0) verify = 0;

        return (total, verify);
    }

    /// <summary>A kind's open blockers in one round, zero where the round leaves it out.</summary>
    private static int Open(JsonElement round, string kind) =>
        round.TryGetProperty(kind, out var counts) && counts.ValueKind is JsonValueKind.Object ? Count(counts, "open") ?? 0 : 0;

    /// <summary>A whole number from zero up, or null.</summary>
    private static int? Count(JsonElement node, string name) =>
        node.ValueKind is JsonValueKind.Object
        && node.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.Number
        && value.TryGetInt32(out var count)
        && count >= 0
            ? count
            : null;
}
