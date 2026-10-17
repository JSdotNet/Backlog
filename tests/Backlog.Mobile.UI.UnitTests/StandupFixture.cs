using System.Globalization;

using Backlog.Mobile.UI.Progress;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// One stand-up's worth of work on the task feed, for the Stand-up, goal and impediment
/// screens: two goals, the tasks they count, three impediments — Escalated, New, and one
/// resolved last week — and the stand-up context a desktop with goals on writes. Read on
/// Monday 19 October 2026, so by Week last is 12 to 16 October and next 19 to 23.
/// </summary>
internal static class StandupFixture
{
    /// <summary>Monday 19 October 2026, nine in the morning, UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    public const string Repository = "JSdotNet/Backlog";

    public static readonly Guid PaidId = Guid.Parse("0f6e2c3a-1b4d-4e5f-8a9b-0c1d2e3f4a5b");
    public static readonly Guid SearchId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    public static readonly Guid EscalatedId = Guid.Parse("6a7b8c9d-0e1f-4a2b-8c3d-4e5f6a7b8c9d");
    public static readonly Guid NewId = Guid.Parse("7b8c9d0e-1f2a-4b3c-9d4e-5f6a7b8c9d0e");
    public static readonly Guid ResolvedId = Guid.Parse("8c9d0e1f-2a3b-4c4d-8e5f-6a7b8c9d0e1f");

    public static readonly Guid InvoiceId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    public static readonly Guid BankFileId = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    public static readonly Guid MapFieldsId = Guid.Parse("a1000000-0000-4000-8000-000000000003");
    public static readonly Guid StatementId = Guid.Parse("a1000000-0000-4000-8000-000000000004");
    public static readonly Guid ReconcileId = Guid.Parse("a1000000-0000-4000-8000-000000000005");
    public static readonly Guid RankId = Guid.Parse("a1000000-0000-4000-8000-000000000006");
    public static readonly Guid VatId = Guid.Parse("a1000000-0000-4000-8000-000000000007");

    /// <summary>The whole stand-up, context included: goals are on.</summary>
    public static ScriptedTaskService Service(bool withContext = true)
    {
        var service = new ScriptedTaskService();
        foreach (var change in Tasks()) service.Append(change);
        foreach (var change in Documents()) service.Append(change);
        if (withContext) service.Append(Context());
        return service;
    }

    public static IEnumerable<TaskChange> Tasks()
    {
        yield return Task(InvoiceId, "Invoice PDF export", "done", completedOn: new DateOnly(2026, 10, 14), effort: 5, pullRequest: 1338);
        yield return Task(BankFileId, "Bank file import", "done", "bank-integration", completedOn: new DateOnly(2026, 10, 14), effort: 3);
        yield return Task(MapFieldsId, "Map the bank's fields", "in_progress", "bank-integration", effort: 5, pullRequest: 1341);
        yield return Task(StatementId, "Statement format", "ready", "bank-integration", blockedSince: new DateOnly(2026, 10, 15), order: 1);
        yield return Task(ReconcileId, "Reconcile payments", "ready", "bank-integration", effort: 8, order: 3);
        yield return Task(RankId, "Rank results by heading", "done", "devbook-search", completedOn: new DateOnly(2026, 10, 13), effort: 2);
        yield return Task(VatId, "VAT rate per line item", "done", completedOn: new DateOnly(2026, 10, 16), effort: 2);
    }

    public static IEnumerable<TaskChange> Documents()
    {
        yield return Whole(PaidId, "goal", "Customers get paid faster", Now.AddDays(-7), $$"""
            {"version":1,"id":"{{PaidId}}","title":"Customers get paid faster","dueDate":"2026-10-30",
             "attachments":[
               {"kind":"Plan","plan":"bank-integration","attachedOn":"2026-10-05"},
               {"kind":"Task","taskId":"{{InvoiceId}}","attachedOn":"2026-10-05"},
               {"kind":"PullRequest","repository":"{{Repository}}","number":1352,"attachedOn":"2026-10-05"}],
             "createdAt":"2026-10-05T09:00:00+00:00"}
            """);
        yield return Whole(SearchId, "goal", "Devbook search ships", Now.AddDays(-7), $$"""
            {"version":1,"id":"{{SearchId}}","title":"Devbook search ships","dueDate":"2026-10-23",
             "attachments":[{"kind":"Plan","plan":"devbook-search","attachedOn":"2026-10-06"}],
             "createdAt":"2026-10-06T09:00:00+00:00"}
            """);
        yield return Whole(EscalatedId, "impediment", "Bank sample files missing", Now.AddDays(-3), $$"""
            {"version":1,"id":"{{EscalatedId}}","title":"Bank sample files missing","reportedTo":"account manager",
             "checkAgain":"2026-10-20","note":"Support silent for 2 days.",
             "history":[{"state":"New","on":"2026-10-14"},{"state":"Reported","on":"2026-10-15","with":"bank support"},
                        {"state":"Escalated","on":"2026-10-16","with":"account manager"}],
             "holdsBack":["{{StatementId}}","{{ReconcileId}}"],"createdAt":"2026-10-14T09:00:00+00:00"}
            """);
        yield return Whole(NewId, "impediment", "Credit note numbering unclear", Now.AddDays(-1), $$"""
            {"version":1,"id":"{{NewId}}","title":"Credit note numbering unclear",
             "history":[{"state":"New","on":"2026-10-16"}],"holdsBack":[],"createdAt":"2026-10-16T09:00:00+00:00"}
            """);
        yield return Whole(ResolvedId, "impediment", "Staging VPN certificate expired", Now.AddDays(-5), $$"""
            {"version":1,"id":"{{ResolvedId}}","title":"Staging VPN certificate expired",
             "history":[{"state":"New","on":"2026-10-12"},{"state":"Resolved","on":"2026-10-14","with":"Ops"}],
             "holdsBack":[],"createdAt":"2026-10-12T09:00:00+00:00"}
            """);
    }

    public static TaskChange Context(IterationLength length = IterationLength.Week) =>
        Whole(ProgressViewProjection.StandUpContextId, "stand-up-context", "Stand-up context", Now.AddHours(-1), ProgressDocuments.WriteContext(new StandUpContextDto(
            Now.AddHours(-1),
            [
                new GoalPullRequestDto(Repository, "1341", LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(1, 2)),
                new GoalPullRequestDto(Repository, "1338", LinkedPullRequestState.Merged, new DateOnly(2026, 10, 14), new GoalPullRequestChecksDto(12, 0, 0, []), new GoalPullRequestApprovalsDto(2, 2)),
                new GoalPullRequestDto(Repository, "1352", LinkedPullRequestState.Merged, new DateOnly(2026, 10, 13), new GoalPullRequestChecksDto(9, 0, 5, []), new GoalPullRequestApprovalsDto(2, 2)),
            ],
            18m,
            null,
            length,
            null)));

    private static TaskChange Task(
        Guid id,
        string title,
        string status,
        string? plan = null,
        DateOnly? completedOn = null,
        int? effort = null,
        DateOnly? blockedSince = null,
        int? pullRequest = null,
        int order = 0)
    {
        var change = TestTasks.Task(title, Now.AddDays(-10), status: status, id: id, tags: plan is null ? [] : [$"+{plan}"], repoIds: [Repository], order: order);
        return change with
        {
            Task = change.Task with
            {
                CompletedOn = completedOn,
                Effort = effort,
                BlockedSince = blockedSince,
                ProjectionRefs = pullRequest is { } number
                    ? [new ProjectionPayload(Repository, number.ToString(CultureInfo.InvariantCulture), "pull-request")]
                    : []
            }
        };
    }

    private static TaskChange Whole(Guid id, string type, string title, DateTimeOffset at, string content) =>
        TestTasks.Task(title, at, type: type, id: id, contentMd: content, status: "draft");
}
