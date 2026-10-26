using System.Globalization;
using System.Text;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel.Ai;

namespace Backlog.Modules.Progress.UI;

/// <summary>
/// The Goals view's answer to <see cref="IAiContentSource"/>: the stand-up as
/// <see cref="IStandUp"/> reads it now (<c>.devbook/domain/progress/domain.md#stand-up</c>) —
/// the goals with their progress and standing, then Finished, Planned and the open
/// impediments.
/// </summary>
/// <remarks>
/// <para>
/// The same read the view makes, <see cref="IStandUp.ReadAsync(CancellationToken)"/> by the
/// person's own iteration length, so a question asked on the view is answered from the
/// stand-up the view draws. Which card is expanded, which goal is being edited and which
/// impediment's picker is open are the reader arranging the screen, and none of it reaches
/// here.
/// </para>
/// <para>
/// The two windows are the scope — last is what Finished and each goal's <c>in last</c>
/// count, next is what Planned plans — so the first line names them. The line under it
/// carries the stand-up's own totals: planned effort against capacity, and how many
/// impediments were resolved in last, which no record says.
/// </para>
/// <para>
/// The goals go first and always go: they are pinned, so no question's words rank one out,
/// and the items and impediments compete for the rest of the budget the way every source's
/// records do. A goal is the subject of most questions asked on this view, and a body that
/// left one out would be telling the assistant the person has fewer goals than they do.
/// </para>
/// <para>
/// A stand-up that cannot be read is a body that says so under the first line, never a
/// throw: the reader is about to send a question and deserves to know it will be answered
/// from nothing.
/// </para>
/// </remarks>
internal sealed class GoalsAiContentSource(IStandUp standUp) : IAiContentSource
{
    public string AreaKey => "goals";

    public string AreaTitle => "Goals";

    public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var read = await standUp.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (read.IsFailure)
        {
            return AiContentBudget.Compose<GoalsRecord>(
                AreaKey,
                AreaTitle,
                [],
                record => record.Text,
                request.Question,
                request.BudgetCharacters,
                note: $"The stand-up could not be read: {read.Error.Message}");
        }

        var reading = read.Value;

        IReadOnlyList<GoalsRecord> records =
        [
            .. reading.Goals.Select(goal => new GoalsRecord(Text(goal), true)),
            .. Items("Finished", reading.Finished).Select(text => new GoalsRecord(text, false)),
            .. Items("Planned", reading.Planned).Select(text => new GoalsRecord(text, false)),
            .. reading.Impediments.Select(impediment => new GoalsRecord(Text(impediment), false))
        ];

        return AiContentBudget.Compose(
            AreaKey,
            $"{AreaTitle} (stand-up: last {Window(reading.Last)}, next {Window(reading.Next)})",
            records,
            record => record.Text,
            request.Question,
            request.BudgetCharacters,
            pinned: record => record.Goal,
            note: Totals(reading));
    }

    /// <summary>"Goal: title, due, done of total, in last, doing, blocked, to do, standing,
    /// landing."</summary>
    internal static string Text(StandUpGoalDto goal)
    {
        var progress = goal.Progress;
        var text = new StringBuilder("Goal: ").Append(goal.Title.Trim());

        text.Append(", due ").Append(AiContentBudget.Date(goal.DueDate));
        if (goal.DueIsIterationEnd) text.Append(" (end of iteration)");
        text.Append(", ").Append(Number(progress.Done)).Append(" of ").Append(Number(progress.Total)).Append(" done");
        text.Append(", ").Append(Number(progress.DoneInWindow)).Append(" in last");
        text.Append(", ").Append(Number(progress.Doing)).Append(" doing");
        text.Append(", ").Append(Number(progress.Blocked)).Append(" blocked");
        text.Append(", ").Append(Number(progress.ToDo)).Append(" to do");
        text.Append(goal.Standing == GoalStanding.AtRisk ? ", at risk" : ", on track");
        text.Append(goal.LandsOn is { } lands ? ", lands " + AiContentBudget.Date(lands) : ", no landing date");

        return text.ToString();
    }

    /// <summary>"Block: title, kind, repository, effort, due, goal, done" — one line per item
    /// of a Finished or Planned block, in the stand-up's own order.</summary>
    internal static IEnumerable<string> Items(string block, IReadOnlyList<StandUpGroupDto> groups) =>
        groups.SelectMany(group => group.Items.Select(item => Text(block, item, group.GoalTitle)));

    internal static string Text(string block, StandUpItemDto item, string? goalTitle)
    {
        var text = new StringBuilder(block).Append(": ");

        if (item.Kind == GoalAttachmentKind.PullRequest)
        {
            // The title already is owner/name#number, so the repository is not said twice.
            text.Append("pull request ").Append(item.Title.Trim());
        }
        else
        {
            text.Append(item.Title.Trim()).Append(", task");
            if (!string.IsNullOrWhiteSpace(item.Repository)) text.Append(", repository ").Append(item.Repository.Trim());
        }

        if (item.Effort is { } effort) text.Append(", effort ").Append(Number(effort));
        if (item.DueOn is { } due) text.Append(", due ").Append(AiContentBudget.Date(due));
        text.Append(goalTitle is null ? ", no goal" : ", for goal " + goalTitle.Trim());
        if (item.DoneOn is { } done) text.Append(", done ").Append(AiContentBudget.Date(done));

        return text.ToString();
    }

    /// <summary>"Impediment: title, state, who it is with, age, check again, the goal it
    /// holds back; then its note on its own line when it has one."</summary>
    internal static string Text(StandUpImpedimentDto impediment)
    {
        var text = new StringBuilder("Impediment: ").Append(impediment.Title.Trim());

        text.Append(", ").Append(State(impediment.State));
        text.Append(string.IsNullOrWhiteSpace(impediment.With) ? ", not reported yet" : ", with " + impediment.With.Trim());
        text.Append(", ").Append(Number(impediment.DaysOpen)).Append(impediment.DaysOpen == 1 ? " worked day open" : " worked days open");
        if (impediment.CheckAgain is { } again) text.Append(", check again ").Append(AiContentBudget.Date(again));
        if (!string.IsNullOrWhiteSpace(impediment.GoalTitle)) text.Append(", holds back goal ").Append(impediment.GoalTitle.Trim());
        if (!string.IsNullOrWhiteSpace(impediment.Note)) text.Append('\n').Append(impediment.Note.Trim());

        return text.ToString();
    }

    /// <summary>The stand-up's totals, which no record carries.</summary>
    private static string Totals(StandUpDto reading)
    {
        var effort = Number(reading.PlannedEffort);
        var planned = reading.Capacity is { } capacity
            ? $"Planned effort {effort} of a capacity of {Number(capacity)} points."
            : $"Planned effort {effort} points, no capacity known.";
        var resolved = reading.ResolvedInLast == 1
            ? "1 impediment resolved in last."
            : $"{Number(reading.ResolvedInLast)} impediments resolved in last.";

        return planned + " " + resolved;
    }

    /// <summary>"week 2026-10-12 to 2026-10-16", or "day 2026-10-16" for a one-day window.</summary>
    private static string Window(IterationWindowDto window)
    {
        var length = window.Length switch
        {
            IterationLength.Day => "day",
            IterationLength.Week => "week",
            IterationLength.TwoWeeks => "two weeks",
            IterationLength.Month => "month",
            _ => window.Length.ToString().ToLowerInvariant()
        };

        return window.First == window.Last
            ? $"{length} {AiContentBudget.Date(window.First)}"
            : $"{length} {AiContentBudget.Date(window.First)} to {AiContentBudget.Date(window.Last)}";
    }

    private static string State(ImpedimentState state) => state switch
    {
        ImpedimentState.New => "new",
        ImpedimentState.Reported => "reported",
        ImpedimentState.Escalated => "escalated",
        ImpedimentState.BeingHandled => "being handled",
        ImpedimentState.Resolved => "resolved",
        _ => state.ToString().ToLowerInvariant()
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>One line of the body: a goal, which is pinned, or an item or impediment,
    /// which competes for what is left.</summary>
    private sealed record GoalsRecord(string Text, bool Goal);
}
