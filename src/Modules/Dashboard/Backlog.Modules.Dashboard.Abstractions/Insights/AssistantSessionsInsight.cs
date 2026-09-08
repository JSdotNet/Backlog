namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// One row of the sessions breakdown — a machine when every machine is in view, an
/// assistant when one machine is focused.
/// <para>
/// One row type for both, because the two are the same four measures under a different
/// heading, and two records would only mean two places for the arithmetic to drift.
/// The part names the column; the row carries the name.
/// </para>
/// </summary>
/// <param name="Key">
/// What makes this row one row: the machine's stable id, or the assistant's label once a
/// machine is focused. Beside <paramref name="Name"/> rather than instead of it, and for
/// the reason the whole feature exists — two machines can share a name, and a table keyed
/// on the name would hand a rendering component two siblings with one key. What that
/// costs is not a merged row but a dead surface, because Blazor's keyed diff throws.
/// </param>
/// <param name="Name">The machine or the assistant this row is about.</param>
/// <param name="Sessions">How many sessions the row covers.</param>
/// <param name="ActiveTime">How long they ran, clipped to the window. Sessions with no
/// recorded start contribute nothing here while still counting in
/// <paramref name="Sessions"/>.</param>
/// <param name="LastActivityAt">The most recent moment anything moved in this row.</param>
public sealed record AssistantSessionRow(
    string Key,
    string Name,
    int Sessions,
    TimeSpan ActiveTime,
    DateTimeOffset? LastActivityAt);

/// <summary>
/// What the assistants have been doing on the scoped machines, over the scoped window.
/// <para>
/// Every figure here is a floor rather than a total whenever <see cref="Capped"/> is
/// true or <see cref="Unreadable"/> is non-empty, and both travel with the figures for
/// exactly that reason: a capped number presented as a whole one is how a dashboard
/// quietly stops being trusted. <see cref="WithoutStart"/> is the third of the same
/// kind — a session whose start was never written down is real and is counted, but it
/// cannot contribute a duration, and saying so is the difference between an
/// understated figure and a dishonest one.
/// </para>
/// </summary>
/// <param name="Sessions">How many sessions touched the window.</param>
/// <param name="ActiveTime">Their time inside the window, summed. Overlapping sessions
/// are summed rather than merged: this is time spent working with the assistants, not
/// wall-clock time during which one was open.</param>
/// <param name="LastActivityAt">The most recent activity in the scoped set, or null
/// when there was none.</param>
/// <param name="WithoutStart">How many of <paramref name="Sessions"/> had no recorded
/// start and so added nothing to <paramref name="ActiveTime"/>.</param>
/// <param name="Capped">Whether the source stopped short of everything it holds.</param>
/// <param name="CapPerAssistant">How many sessions per assistant the source stopped at,
/// so the sentence admitting the cap can name the number instead of the surface keeping
/// a copy of it.</param>
/// <param name="Unreadable">The sources that could not be read, by name.</param>
/// <param name="Breakdown">Per machine when every machine is in view, per assistant
/// when one is focused. Ordered by sessions descending, then by name.</param>
public sealed record AssistantSessionsInsight(
    int Sessions,
    TimeSpan ActiveTime,
    DateTimeOffset? LastActivityAt,
    int WithoutStart,
    bool Capped,
    int CapPerAssistant,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<AssistantSessionRow> Breakdown)
{
    /// <summary>
    /// How many sessions fell in each ISO week of the window, oldest first, one point
    /// per week whether anything happened in it or not.
    /// <para>
    /// A session is counted in the week it <em>last moved</em>. That is the only week
    /// every session has — the start is optional and no end is recorded at all — so
    /// any other rule would either invent an instant or drop the sessions missing one.
    /// The price is that a session running across a week boundary is one mark in the
    /// later week rather than a mark in each, and the surface says so beside the
    /// columns rather than leaving a reader to find the sum that does not add up.
    /// </para>
    /// <para>
    /// Beside the primary constructor rather than in it, the escape
    /// <c>ActivityPullRequest.ReviewTurnaround</c> and
    /// <c>GitHubReviewedPullRequest.ReviewTurnaround</c> already take: fixtures and
    /// adapters construct this record positionally, and a ninth parameter would break
    /// every one of them to add something none of them has an opinion about.
    /// </para>
    /// </summary>
    public IReadOnlyList<InsightPoint> SessionsPerWeek { get; init; } = [];

    public static AssistantSessionsInsight Empty { get; } =
        new(0, TimeSpan.Zero, null, 0, false, 0, [], []) { SessionsPerWeek = [] };
}
