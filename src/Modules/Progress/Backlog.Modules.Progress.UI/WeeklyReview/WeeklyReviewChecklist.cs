namespace Backlog.Modules.Progress.UI.WeeklyReview;

/// <summary>
/// The three stages of the weekly review, in the order it walks them
/// (<c>.devbook/domain/progress/features.md#the-weekly-review</c>) — the same three the
/// <c>weekly-review</c> skill reports.
/// </summary>
public enum WeeklyReviewStage
{
    /// <summary>Empty the trays: the Inbox, the Brain's inbox area, the deferred items
    /// due this week and those with no review date.</summary>
    GetClear,

    /// <summary>Bring the commitments up to date: plans, project notes, waiting-for,
    /// overdue dates and goals.</summary>
    GetCurrent,

    /// <summary>Look further out: someday notes, notes due for review, quiet areas.</summary>
    GetCreative,
}

/// <summary>What a review line's button does when pressed.</summary>
public enum WeeklyReviewActionKind
{
    /// <summary>Give the Inbox the screen, to clarify or look again.</summary>
    OpenInbox,

    /// <summary>Open a Brain note in the Devbook; the target is its path.</summary>
    OpenNote,

    /// <summary>Open a backlog entry in the Tasks view; the target is its id.</summary>
    OpenTask,

    /// <summary>Make a plan's draft step ready, so the plan has a next action; the
    /// target is the entry's id.</summary>
    MarkNextAction,

    /// <summary>Move a Brain note to the archive area; the target is its path.</summary>
    ArchiveNote,

    /// <summary>Show the Goals view.</summary>
    OpenGoals,

    /// <summary>Open the Tasks view on a plan tag with no entry yet; the target is the
    /// tag.</summary>
    OpenPlan,
}

/// <summary>One button on a review line.</summary>
/// <param name="Kind">What the press does.</param>
/// <param name="Label">The button's words.</param>
/// <param name="Target">What it acts on: a note's path, an entry's id or a plan tag;
/// null for <see cref="WeeklyReviewActionKind.OpenInbox"/> and
/// <see cref="WeeklyReviewActionKind.OpenGoals"/>.</param>
public sealed record WeeklyReviewAction(WeeklyReviewActionKind Kind, string Label, string? Target = null)
{
    /// <summary>Whether the press changes something rather than only showing it: these
    /// are the presses the review reads again after.</summary>
    public bool Writes => Kind is WeeklyReviewActionKind.MarkNextAction or WeeklyReviewActionKind.ArchiveNote;
}

/// <summary>One finding of the review: what it is about, the check that found it, and
/// the actions that answer it.</summary>
/// <param name="Key">Stable within one reading — the kind of check and what it is
/// about — so a test or a busy state can name a line.</param>
/// <param name="Stage">The stage it belongs to.</param>
/// <param name="Subject">The entry's title, the note's path, the plan's tag.</param>
/// <param name="Check">What to do about it, in the skill's words.</param>
/// <param name="Actions">Its buttons, the first the usual answer.</param>
public sealed record WeeklyReviewLine(
    string Key,
    WeeklyReviewStage Stage,
    string Subject,
    string Check,
    IReadOnlyList<WeeklyReviewAction> Actions);

/// <summary>
/// One reading of the review: every line, by stage, and the checks that could not be
/// made because a source was not there — the Brain unreadable, goals not composed.
/// </summary>
public sealed record WeeklyReviewChecklist(IReadOnlyList<WeeklyReviewLine> Lines, IReadOnlyList<string> Unavailable)
{
    /// <summary>A reading with nothing in it.</summary>
    public static WeeklyReviewChecklist Empty { get; } = new([], []);

    /// <summary>The stages in the order the review walks them.</summary>
    public static IReadOnlyList<WeeklyReviewStage> Stages { get; } =
        [WeeklyReviewStage.GetClear, WeeklyReviewStage.GetCurrent, WeeklyReviewStage.GetCreative];

    /// <summary>The lines of <paramref name="stage"/>, in the order they were found.</summary>
    public IReadOnlyList<WeeklyReviewLine> LinesOf(WeeklyReviewStage stage) =>
        [.. Lines.Where(line => line.Stage == stage)];

    /// <summary>A stage is done when it has nothing left.</summary>
    public bool IsDone(WeeklyReviewStage stage) => Lines.All(line => line.Stage != stage);

    /// <summary>The stage's name on screen.</summary>
    public static string NameOf(WeeklyReviewStage stage) => stage switch
    {
        WeeklyReviewStage.GetClear => "Get clear",
        WeeklyReviewStage.GetCurrent => "Get current",
        _ => "Get creative",
    };

    /// <summary>What the stage is for, said under its name.</summary>
    public static string PurposeOf(WeeklyReviewStage stage) => stage switch
    {
        WeeklyReviewStage.GetClear => "Empty the Inbox, the Brain's inbox, the deferred items due this week and those with no review date.",
        WeeklyReviewStage.GetCurrent => "Give every plan a next action, every project note a plan, and chase what is late.",
        _ => "Look again at someday notes, notes due for review and areas gone quiet.",
    };

    /// <summary>The stage's lower-case id, for test ids and keys.</summary>
    public static string SlugOf(WeeklyReviewStage stage) => stage switch
    {
        WeeklyReviewStage.GetClear => "get-clear",
        WeeklyReviewStage.GetCurrent => "get-current",
        _ => "get-creative",
    };
}
