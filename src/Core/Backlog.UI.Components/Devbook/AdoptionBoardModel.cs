namespace Backlog.UI.Components.Devbook;

/// <summary>Where a home of the adoption board sits relative to the pull request.</summary>
public enum AdoptionHomeGroup
{
    /// <summary>A stage worked in the author's session, before the pull request.</summary>
    BeforePullRequest,

    /// <summary>A stage after the pull request: on GitHub and in production.</summary>
    AfterPullRequest,

    /// <summary>A layer underneath the path — not a stage.</summary>
    Underneath
}

/// <summary>The rung a card sits on: the <c>.tech</c> ladder, in the order a usage climbs it.</summary>
public enum AdoptionRung
{
    Candidate,
    Trial,
    Adopted,

    /// <summary>Off to the side of the ladder; drawn in the retired column with its word.</summary>
    Hold,

    Retired
}

/// <summary>How much a card's rating rests on — the <c>evidence</c> field, weakest first.</summary>
public enum AdoptionEvidence
{
    /// <summary>No evidence field: the card shows no evidence mark.</summary>
    Unstated,

    /// <summary>Nothing yet.</summary>
    None,

    /// <summary>It ran, and nothing it produced was published or kept.</summary>
    Fired,

    /// <summary>Some of it is on record; the rest is still to show.</summary>
    Partial,

    /// <summary>The way of working has a record.</summary>
    Recorded
}

/// <summary>One row of the board: a stage of the delivery path or a layer beneath it.</summary>
/// <param name="Key">The host's key, which cards name in <see cref="AdoptionCard.HomeKey"/>.</param>
/// <param name="Name">The display name.</param>
/// <param name="Group">Before the pull request, after it, or underneath.</param>
/// <param name="Number">The stage's two-digit number, or null for a layer.</param>
public sealed record AdoptionHome(string Key, string Name, AdoptionHomeGroup Group, string? Number = null);

/// <summary>One usage on the board.</summary>
/// <param name="Key">The host's key, handed back when the card is picked.</param>
/// <param name="HomeKey">The row it sits in: one home per card.</param>
/// <param name="Title">Its name.</param>
/// <param name="Rung">Its column.</param>
/// <param name="Evidence">What its rating rests on.</param>
/// <param name="Since">When it took its rung, as written.</param>
/// <param name="EvidenceText">What the record is, in the chapter's words.</param>
/// <param name="MovesUpWhen">The one event that would move it up a rung.</param>
/// <param name="Reach">The other homes it reaches, by name.</param>
public sealed record AdoptionCard(
    string Key,
    string HomeKey,
    string Title,
    AdoptionRung Rung,
    AdoptionEvidence Evidence,
    string? Since = null,
    string? EvidenceText = null,
    string? MovesUpWhen = null,
    IReadOnlyList<string>? Reach = null)
{
    /// <summary>Whether the card still needs evidence: on the ladder, and without
    /// a record behind it. Retired and held cards wait on nothing.</summary>
    public bool NeedsEvidence =>
        Rung is AdoptionRung.Candidate or AdoptionRung.Trial or AdoptionRung.Adopted
        && Evidence != AdoptionEvidence.Recorded;
}

/// <summary>
/// What the adoption board draws: its homes in row order and every card on them.
/// Domain-free on purpose — the host reads its own record into this shape, and the
/// storybook draws every state of the board from hand-made samples.
/// </summary>
public sealed record AdoptionBoardModel(IReadOnlyList<AdoptionHome> Homes, IReadOnlyList<AdoptionCard> Cards)
{
    public static AdoptionBoardModel Empty { get; } = new([], []);

    /// <summary>The columns of the ladder grid, left to right.</summary>
    public static IReadOnlyList<AdoptionRung> Columns { get; } =
        [AdoptionRung.Candidate, AdoptionRung.Trial, AdoptionRung.Adopted, AdoptionRung.Retired];

    public IEnumerable<AdoptionCard> CardsAt(AdoptionHome home) =>
        Cards.Where(card => string.Equals(card.HomeKey, home.Key, StringComparison.Ordinal));

    /// <summary>The cards of one cell: held cards share the retired column.</summary>
    public IEnumerable<AdoptionCard> CardsAt(AdoptionHome home, AdoptionRung column) =>
        CardsAt(home).Where(card => Column(card.Rung) == column);

    public static AdoptionRung Column(AdoptionRung rung) => rung == AdoptionRung.Hold ? AdoptionRung.Retired : rung;

    /// <summary>How many cards of a home sit on a rung.</summary>
    public int Count(AdoptionHome home, AdoptionRung rung) => CardsAt(home).Count(card => card.Rung == rung);

    /// <summary>The largest live total (adopted, trial and candidate) of any home:
    /// the full width of a strip tile's bar.</summary>
    public int LargestLiveTotal =>
        Homes.Select(home => CardsAt(home).Count(card => card.Rung is AdoptionRung.Adopted or AdoptionRung.Trial or AdoptionRung.Candidate))
            .DefaultIfEmpty(0)
            .Max();

    public AdoptionFindings Findings => AdoptionFindings.From(this);
}

/// <summary>
/// The three findings over the board, counted from its cards rather than written:
/// how far adopted reaches, how proven trial is, and what waits to move.
/// </summary>
public sealed record AdoptionFindings(
    int Adopted,
    int AdoptedRecorded,
    int AdoptedBefore,
    int AdoptedAfter,
    int AdoptedUnderneath,
    IReadOnlyList<string> HomesWithoutAdopted,
    int Trial,
    int TrialRecorded,
    int TrialPartial,
    int TrialFired,
    int TrialNone,
    int Waiting,
    int WaitingWithEvent)
{
    public static AdoptionFindings From(AdoptionBoardModel board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var groups = board.Homes.ToDictionary(home => home.Key, home => home.Group, StringComparer.Ordinal);
        var adopted = board.Cards.Where(card => card.Rung == AdoptionRung.Adopted && groups.ContainsKey(card.HomeKey)).ToList();
        var trial = board.Cards.Where(card => card.Rung == AdoptionRung.Trial && groups.ContainsKey(card.HomeKey)).ToList();
        var waiting = board.Cards
            .Where(card => card.Rung is AdoptionRung.Trial or AdoptionRung.Candidate && groups.ContainsKey(card.HomeKey))
            .ToList();

        int AdoptedIn(AdoptionHomeGroup group) => adopted.Count(card => groups[card.HomeKey] == group);
        int TrialWith(AdoptionEvidence evidence) => trial.Count(card => card.Evidence == evidence);

        return new AdoptionFindings(
            adopted.Count,
            adopted.Count(card => card.Evidence == AdoptionEvidence.Recorded),
            AdoptedIn(AdoptionHomeGroup.BeforePullRequest),
            AdoptedIn(AdoptionHomeGroup.AfterPullRequest),
            AdoptedIn(AdoptionHomeGroup.Underneath),
            [.. board.Homes.Where(home => board.Count(home, AdoptionRung.Adopted) == 0).Select(home => home.Name)],
            trial.Count,
            TrialWith(AdoptionEvidence.Recorded),
            TrialWith(AdoptionEvidence.Partial),
            TrialWith(AdoptionEvidence.Fired),
            // A trial card with no evidence field has nothing on record either.
            trial.Count(card => card.Evidence is AdoptionEvidence.None or AdoptionEvidence.Unstated),
            waiting.Count,
            waiting.Count(card => !string.IsNullOrWhiteSpace(card.MovesUpWhen)));
    }
}
