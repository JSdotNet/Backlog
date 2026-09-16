namespace Backlog.Infrastructure.Claude;

/// <summary>
/// What one actor did in Claude Code on one day, as far as spend is concerned:
/// the per-model token and cost rows and nothing else.
/// <para>
/// An empty list is a real answer — the day was read and this actor is not in
/// it — and it is different from the cache holding nothing. The port returns null
/// for the second and this record with no rows for the first, so a quiet day is
/// not re-fetched for ever on the suspicion that it might not have been read.
/// </para>
/// </summary>
public sealed record ClaudeCodeSettledDay(IReadOnlyList<ClaudeCodeModelUsage> Models)
{
    public static ClaudeCodeSettledDay Empty { get; } = new([]);
}

/// <summary>
/// Keeps one actor's Claude Code spend for days that are over on disk, so a
/// dashboard read asks Anthropic only about the days that can still change.
/// <para>
/// The endpoint answers one day per call, and the trend window is seven months,
/// so without this every dashboard open is about two hundred calls per account
/// to learn figures that were the same yesterday. A day more than a short
/// settlement horizon behind us is read once and remembered; today and yesterday
/// are asked for every time, because Anthropic aggregates a day over the hours
/// after it ends.
/// </para>
/// <para>
/// Keyed on the account <em>and</em> the actor rather than the account alone. The
/// report covers the whole organization, and only the named actor's rows are
/// kept — the other actors' spend is not this person's and does not belong on
/// this person's disk. Changing which actor an account is narrowed to therefore
/// starts a fresh set of days rather than answering with the previous actor's.
/// </para>
/// <para>
/// Declared and implemented in this project, unlike the GitHub caches, because
/// this project already decides where its own bytes land — <see cref="ClaudeSettingsStore"/>
/// names its file itself — and a host hands the root over the same way it hands
/// the settings path.
/// </para>
/// <para>
/// Synchronous and non-throwing: an entry that cannot be read is fetched, and
/// one that cannot be written is fetched again next time.
/// </para>
/// </summary>
public interface IClaudeCodeUsageCache
{
    /// <summary>What is stored for this account, actor and day, or null when
    /// nothing is — including when what is stored cannot be read or was written
    /// by an older version of this app.</summary>
    ClaudeCodeSettledDay? TryRead(ClaudeAccount account, string actor, DateOnly day);

    /// <summary>Stores one settled day. Failure is not reported, because there is
    /// nothing a caller could usefully do about it.</summary>
    void Write(ClaudeAccount account, string actor, DateOnly day, ClaudeCodeSettledDay usage);

    /// <summary>Drops every stored day for every account, so the next read asks
    /// Anthropic about all of them again.</summary>
    void Forget();
}
