using System.Globalization;
using System.Text.Json;

using Backlog.Mobile.UI.Progress;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

namespace Backlog.Mobile.WebHarness;

/// <summary>
/// A stand-up for the browser harness to show without a desktop that has goals on: three
/// goals, four impediments — three open and one resolved last week — a stand-up context
/// with the pull requests' statuses and a pace, and the synced tasks the goals count, all
/// dated from today so Finished and Planned always have something in them
/// (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>).
/// <para>
/// Written straight into the phone's Progress and task views, in the shapes a desktop's
/// documents fold into, and only while the Progress view holds no goal: a real pull later
/// folds over the rows by the usual last-write-wins, and a harness that already has goals is
/// left alone. Harness only, and only while <c>Harness:SeedStandup</c> is on. It writes
/// before anything reads the views, because both projections read their rows once.
/// </para>
/// </summary>
internal static class StandupSeed
{
    private const string Repository = "JSdotNet/Backlog";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Seeds the views when the harness's configuration asks for it and the Progress
    /// view holds no goal yet.</summary>
    public static async Task SeedAsync(IServiceProvider services, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        var progress = services.GetRequiredService<IProgressViewStore>();
        if (progress.ReadRows().Any(row => row.Kind == ProgressFold.GoalType)) return;

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var (last, next) = StandUpReading.Windows(today, IterationLength.Week, null, WorkingHours.Default);

        // Days inside the last window, the next one, and the weeks before them.
        var lastMonday = last.First;
        DateOnly LastWeek(int day) => lastMonday.AddDays(Math.Min(day, last.Last.DayNumber - last.First.DayNumber));
        DateOnly WeeksAgo(int weeks, int day) => lastMonday.AddDays(-7 * weeks + day);

        var tasks = new List<TaskViewRow>();
        Guid Task(string title, string status, string? plan = null, int? effort = null, DateOnly? completedOn = null,
            DateOnly? blockedSince = null, DateOnly? dueOn = null, int? pullRequest = null, int order = 0)
        {
            var id = Guid.CreateVersion7(now);
            var payload = new TaskPayload(
                title, string.Empty, "task", status, "medium", order, null, now.AddDays(-21), null, null,
                dueOn, null, null, null, null, effort, null, null, null,
                plan is null ? [] : [$"+{plan}"],
                [Repository], [], [], [],
                pullRequest is { } number
                    ? [new ProjectionPayload(Repository, number.ToString(CultureInfo.InvariantCulture), EntryProjectionDto.PullRequestTargetType)]
                    : [],
                CompletedOn: completedOn,
                StartedOn: status == "in_progress" ? today.AddDays(-3) : null,
                BlockedSince: blockedSince);

            tasks.Add(new TaskViewRow(id, now, null, 0, payload));
            return id;
        }

        // Customers get paid faster: a plan, three direct tasks and two pull requests.
        var iban = Task("Customer search by IBAN", "in_progress", "bank-integration", effort: 3, pullRequest: 1341);
        var mapping = Task("Bank import mapping", "ready", "bank-integration", blockedSince: today.AddDays(-2), order: 1);
        var matching = Task("Payment matching rules", "ready", "bank-integration", effort: 8, order: 2);
        var review = Task("Match review screen", "ready", "bank-integration", effort: 5, order: 3);
        Task("Bank file import", "done", "bank-integration", effort: 3, completedOn: WeeksAgo(1, 2));
        Task("Statement formats", "done", "bank-integration", effort: 2, completedOn: WeeksAgo(2, 3));
        Task("Import history per account", "done", "bank-integration", effort: 2, completedOn: LastWeek(3));
        var invoices = Task("Invoice PDF export", "done", effort: 5, completedOn: LastWeek(1), pullRequest: 1338);
        var reminders = Task("Reminder emails", "done", effort: 3, completedOn: LastWeek(2), pullRequest: 1335);
        var recurring = Task("Recurring invoices", "in_progress", effort: 5, pullRequest: 1358);

        // Devbook search ships: one plan, nearly done.
        Task("Index chapters", "done", "devbook-search", effort: 3, completedOn: WeeksAgo(2, 1));
        Task("Search the headings", "done", "devbook-search", effort: 2, completedOn: WeeksAgo(1, 4));
        Task("Rank results by heading", "done", "devbook-search", effort: 3, completedOn: LastWeek(0), pullRequest: 1346);
        Task("Highlight matches", "done", "devbook-search", effort: 2, completedOn: LastWeek(4));
        Task("Search in the mobile app", "ready", "devbook-search", effort: 3);

        // Phone capture in daily use: one plan, early on.
        Task("Quick-capture widget", "done", "phone-capture", effort: 3, completedOn: LastWeek(2), pullRequest: 1349);
        Task("Capture from the share sheet", "ready", "phone-capture", effort: 5);
        Task("Voice capture offline", "ready", "phone-capture", effort: 8, order: 1);
        Task("Capture to a plan", "ready", "phone-capture", effort: 3, order: 2);

        // No goal: work the stand-up still reads.
        Task("VAT rate per line item", "done", effort: 2, completedOn: LastWeek(3));
        Task("Renew the signing certificate", "ready", effort: 1, dueOn: next.First.AddDays(2));

        await services.GetRequiredService<ITaskViewStore>().SaveAsync(tasks, cancellationToken: cancellationToken);

        var paid = Guid.CreateVersion7(now);
        var search = Guid.CreateVersion7(now);
        var capture = Guid.CreateVersion7(now);
        var setOn = lastMonday.AddDays(-14);

        var rows = new List<ProgressViewRow>
        {
            Row(paid, ProgressFold.GoalType, now, Goal(paid, "Customers get paid faster", today.AddDays(11), now.AddDays(-30), setOn,
                new { kind = "Plan", plan = "bank-integration" },
                new { kind = "Task", taskId = invoices },
                new { kind = "Task", taskId = reminders },
                new { kind = "Task", taskId = recurring },
                new { kind = "PullRequest", repository = Repository, number = 1352 },
                new { kind = "PullRequest", repository = Repository, number = 1355 })),
            Row(search, ProgressFold.GoalType, now, Goal(search, "Devbook search ships", today.AddDays(4), now.AddDays(-29), setOn,
                new { kind = "Plan", plan = "devbook-search" })),
            Row(capture, ProgressFold.GoalType, now, Goal(capture, "Phone capture in daily use", today.AddDays(42), now.AddDays(-28), setOn,
                new { kind = "Plan", plan = "phone-capture" })),
        };

        var bank = Guid.CreateVersion7(now);
        rows.Add(Row(bank, ProgressFold.ImpedimentType, now, Impediment(bank, "Bank sample files missing", now.AddDays(-5),
            reportedTo: "account manager", handledBy: null, checkAgain: today.AddDays(1),
            note: "Support silent for 2 days; emailed the account manager.",
            [("New", today.AddDays(-5), null), ("Reported", today.AddDays(-4), "bank support"), ("Escalated", today.AddDays(-3), "account manager")],
            [mapping, matching, review])));

        var runner = Guid.CreateVersion7(now);
        rows.Add(Row(runner, ProgressFold.ImpedimentType, now, Impediment(runner, "Android build runner flaky", now.AddDays(-4),
            reportedTo: "platform team", handledBy: "Platform team", checkAgain: today.AddDays(2), note: null,
            [("New", today.AddDays(-4), null), ("Reported", today.AddDays(-4), "platform team"), ("BeingHandled", today.AddDays(-2), "Platform team")],
            [recurring])));

        var credit = Guid.CreateVersion7(now);
        rows.Add(Row(credit, ProgressFold.ImpedimentType, now, Impediment(credit, "Credit note numbering unclear", now.AddDays(-1),
            reportedTo: null, handledBy: null, checkAgain: null, note: null,
            [("New", today.AddDays(-1), null)],
            [iban])));

        var vpn = Guid.CreateVersion7(now);
        rows.Add(Row(vpn, ProgressFold.ImpedimentType, now, Impediment(vpn, "Staging VPN certificate expired", now.AddDays(-10),
            reportedTo: "ops", handledBy: "Ops", checkAgain: null, note: null,
            [("New", WeeksAgo(1, 2), null), ("Reported", WeeksAgo(1, 2), "ops"), ("Resolved", LastWeek(1), "Ops")],
            [])));

        rows.Add(Row(ProgressViewProjection.StandUpContextId, ProgressFold.StandUpContextType, now, ProgressDocuments.WriteContext(new StandUpContextDto(
            now,
            [
                Pull(1341, LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(1, 2)),
                Pull(1358, LinkedPullRequestState.Draft, null, new GoalPullRequestChecksDto(9, 0, 5, []), new GoalPullRequestApprovalsDto(0, 2)),
                Pull(1338, LinkedPullRequestState.Merged, LastWeek(1), new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(2, 2)),
                Pull(1335, LinkedPullRequestState.Merged, LastWeek(2), new GoalPullRequestChecksDto(12, 0, 0, []), new GoalPullRequestApprovalsDto(2, 2)),
                Pull(1346, LinkedPullRequestState.Merged, LastWeek(0), new GoalPullRequestChecksDto(11, 0, 0, []), new GoalPullRequestApprovalsDto(1, 1)),
                Pull(1349, LinkedPullRequestState.Merged, LastWeek(2), new GoalPullRequestChecksDto(10, 0, 0, []), new GoalPullRequestApprovalsDto(1, 1)),
                Pull(1352, LinkedPullRequestState.Merged, LastWeek(3), new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(2, 2)),
                Pull(1355, LinkedPullRequestState.Merged, WeeksAgo(1, 1), new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(2, 2)),
            ],
            PointsPerWeek: 18m,
            Week: null,
            Length: IterationLength.Week,
            TwoWeekStart: null))));

        await progress.SaveAsync(rows, cancellationToken: cancellationToken);
    }

    private static GoalPullRequestDto Pull(int number, LinkedPullRequestState state, DateOnly? mergedOn, GoalPullRequestChecksDto checks, GoalPullRequestApprovalsDto approvals) =>
        new(Repository, number.ToString(CultureInfo.InvariantCulture), state, mergedOn, checks, approvals);

    private static ProgressViewRow Row(Guid id, string kind, DateTimeOffset at, string content) => new(id, kind, at, null, 0, content);

    /// <summary>A <c>goal</c> document in the shape the desktop's store writes one.</summary>
    private static string Goal(Guid id, string title, DateOnly due, DateTimeOffset createdAt, DateOnly attachedOn, params object[] attachments) =>
        JsonSerializer.Serialize(new
        {
            version = 1,
            id,
            title,
            dueDate = due,
            attachments = attachments.Select(attachment => Attached(attachment, attachedOn)),
            createdAt
        }, Json);

    private static Dictionary<string, object?> Attached(object attachment, DateOnly attachedOn)
    {
        var fields = JsonSerializer.SerializeToElement(attachment, Json)
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value);
        fields["attachedOn"] = attachedOn;
        return fields;
    }

    /// <summary>An <c>impediment</c> document in the shape the desktop's store writes one.</summary>
    private static string Impediment(
        Guid id,
        string title,
        DateTimeOffset createdAt,
        string? reportedTo,
        string? handledBy,
        DateOnly? checkAgain,
        string? note,
        IReadOnlyList<(string State, DateOnly On, string? With)> history,
        IReadOnlyList<Guid> holdsBack) =>
        JsonSerializer.Serialize(new
        {
            version = 1,
            id,
            title,
            reportedTo,
            handledBy,
            checkAgain,
            note,
            history = history.Select(step => new { state = step.State, on = step.On, with = step.With }),
            holdsBack,
            createdAt
        }, Json);
}
