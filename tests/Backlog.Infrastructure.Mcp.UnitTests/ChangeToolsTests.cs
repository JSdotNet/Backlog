using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Changes;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>list_change_sets</c> and <c>propose_restore</c> (local ADR 0061 §5): the first reads the
/// journal through its filters, the second asks the person and nothing more — no tool can
/// restore, because no tool class is handed the restore port.
/// </summary>
public sealed class ChangeToolsTests
{
    private const string Grant = "Delivery (g_abc123)";
    private const string Session = "sess-42";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly ChangeSet AgentRun = new(Guid.NewGuid(), ChangeActor.Agent, "transition on Ship the journal", new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero))
    {
        Caller = Grant,
        SessionId = Session,
        CorrelationId = "corr-1",
    };

    // --- list_change_sets -----------------------------------------------------------------

    [Fact]
    public async Task List_answers_each_change_set_with_its_id_label_actor_caller_time_count_and_restored_mark()
    {
        var restoredBy = Guid.NewGuid();
        var journal = new FakeJournal(
            new ChangeSetEntry(AgentRun, 3),
            new ChangeSetEntry(new ChangeSet(Guid.NewGuid(), ChangeActor.Person, "Edit", AgentRun.StartedAt.AddHours(-1)) { UndoneBy = restoredBy }, 1));

        var payload = await new ChangeTools(new FakeNotifications(), journal).ListChangeSetsAsync(cancellationToken: Cancellation);

        Assert.Equal(2, payload.ChangeSets.Count);
        var agent = payload.ChangeSets[0];
        Assert.Equal(
            (AgentRun.Id, "transition on Ship the journal", "agent", Grant, Session, AgentRun.StartedAt, 3, false, (Guid?)null),
            (agent.Id, agent.Label, agent.Actor, agent.Caller, agent.SessionId, agent.StartedAt, agent.DocumentCount, agent.Restored, agent.RestoredBy));
        var person = payload.ChangeSets[1];
        Assert.Equal(("person", true, (Guid?)restoredBy), (person.Actor, person.Restored, person.RestoredBy));
    }

    [Fact]
    public async Task List_hands_its_filters_to_the_journal_and_caps_the_limit()
    {
        var journal = new FakeJournal();
        var since = new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);
        var until = since.AddHours(2);

        await new ChangeTools(new FakeNotifications(), journal).ListChangeSetsAsync(Session, Grant, since, until, 999, Cancellation);

        Assert.Equal(new ChangeSetQuery(Session, Grant, since, until, ChangeTools.MaxLimit), journal.Queries.Single());
    }

    [Fact]
    public async Task List_defaults_to_fifty_with_no_filter()
    {
        var journal = new FakeJournal();

        await new ChangeTools(new FakeNotifications(), journal).ListChangeSetsAsync(cancellationToken: Cancellation);

        Assert.Equal(new ChangeSetQuery(Limit: 50), journal.Queries.Single());
    }

    [Fact]
    public async Task List_refuses_a_window_that_ends_before_it_starts()
    {
        var since = new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero);

        var refused = await Assert.ThrowsAsync<McpException>(() =>
            new ChangeTools(new FakeNotifications(), new FakeJournal()).ListChangeSetsAsync(since: since, until: since, cancellationToken: Cancellation));

        Assert.Contains("change_sets.window_invalid", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_tools_refuse_in_a_host_with_no_journal()
    {
        var tools = new ChangeTools(new FakeNotifications());

        var listed = await Assert.ThrowsAsync<McpException>(() => tools.ListChangeSetsAsync(cancellationToken: Cancellation));
        var proposed = await Assert.ThrowsAsync<McpException>(() => tools.ProposeRestoreAsync(AgentRun.Id, cancellationToken: Cancellation));

        Assert.Contains("change_journal.not_available", listed.Message, StringComparison.Ordinal);
        Assert.Contains("change_journal.not_available", proposed.Message, StringComparison.Ordinal);
    }

    // --- propose_restore ------------------------------------------------------------------

    [Fact]
    public async Task Propose_asks_the_person_to_restore_or_keep_and_answers_the_notification_id()
    {
        var port = new FakeNotifications();
        var tools = new ChangeTools(port, new FakeJournal(new ChangeSetEntry(AgentRun, 3)));

        var payload = await tools.ProposeRestoreAsync(AgentRun.Id, cancellationToken: Cancellation);

        var (id, draft) = Assert.Single(port.Published);
        Assert.Equal(id, payload.Id);
        Assert.Null(payload.Route);
        Assert.Equal(NotificationKind.Question, draft.Kind);
        Assert.Equal($"Restore 3 changes made by {Grant} (transition on Ship the journal)?", draft.Title);
        Assert.Equal(["Restore", "Keep"], draft.Answer!.Choices.Select(choice => choice.Id));
        Assert.Equal(["Restore", "Keep"], draft.Answer.Choices.Select(choice => choice.Label));
        Assert.False(draft.Answer.AllowMultiple);
        Assert.Null(draft.Answer.Open);
        Assert.Equal("mcp", draft.Source.Context);
        Assert.Equal(AgentRun.Id, RestoreProposals.ChangeSetOf(draft.Source.Key));
        Assert.Equal(new NotificationSubject("mcp", "ask", draft.Source.Key), draft.Subject);
        Assert.Contains(AgentRun.Id.ToString("D"), draft.BodyMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Propose_with_a_session_is_about_that_session_so_get_answer_reads_it_as_it_reads_ask()
    {
        var port = new FakeNotifications();
        var tools = new ChangeTools(port, new FakeJournal(new ChangeSetEntry(AgentRun, 1)));

        var payload = await tools.ProposeRestoreAsync(AgentRun.Id, Session, Cancellation);

        Assert.Equal(new NotificationSubject("sessions", "session", Session), port.Published.Single().Draft.Subject);
        Assert.StartsWith("Restore 1 change made by", port.Published.Single().Draft.Title, StringComparison.Ordinal);

        var answer = await new NotificationTools(port, port).GetAnswerAsync(payload.Id, Session, cancellationToken: Cancellation);
        Assert.Equal("pending", answer.Status);
    }

    [Fact]
    public async Task Two_proposals_of_one_change_set_are_two_questions()
    {
        var port = new FakeNotifications();
        var tools = new ChangeTools(port, new FakeJournal(new ChangeSetEntry(AgentRun, 1)));

        await tools.ProposeRestoreAsync(AgentRun.Id, cancellationToken: Cancellation);
        await tools.ProposeRestoreAsync(AgentRun.Id, cancellationToken: Cancellation);

        Assert.Equal(2, port.Published.Select(published => published.Draft.Source.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Propose_refuses_a_change_set_the_journal_does_not_hold_and_asks_nothing()
    {
        var port = new FakeNotifications();

        var refused = await Assert.ThrowsAsync<McpException>(() =>
            new ChangeTools(port, new FakeJournal()).ProposeRestoreAsync(Guid.NewGuid(), cancellationToken: Cancellation));

        Assert.Contains("change_set.not_found", refused.Message, StringComparison.Ordinal);
        Assert.Empty(port.Published);
    }

    [Fact]
    public async Task Propose_refuses_a_change_set_already_restored()
    {
        var port = new FakeNotifications();
        var journal = new FakeJournal(new ChangeSetEntry(AgentRun with { UndoneBy = Guid.NewGuid() }, 2));

        var refused = await Assert.ThrowsAsync<McpException>(() =>
            new ChangeTools(port, journal).ProposeRestoreAsync(AgentRun.Id, cancellationToken: Cancellation));

        Assert.Contains("change_set.already_restored", refused.Message, StringComparison.Ordinal);
        Assert.Empty(port.Published);
    }

    [Fact]
    public async Task Propose_refuses_a_change_set_that_changed_nothing()
    {
        var port = new FakeNotifications();

        var refused = await Assert.ThrowsAsync<McpException>(() =>
            new ChangeTools(port, new FakeJournal(new ChangeSetEntry(AgentRun, 0))).ProposeRestoreAsync(AgentRun.Id, cancellationToken: Cancellation));

        Assert.Contains("change_set.nothing_to_restore", refused.Message, StringComparison.Ordinal);
        Assert.Empty(port.Published);
    }

    [Fact]
    public void A_long_label_is_shortened_so_the_title_fits_the_notification()
    {
        var entry = new ChangeSetEntry(AgentRun with { Label = "transition on " + new string('x', 120) }, 12);

        var title = RestoreProposals.Title(entry);

        Assert.True(title.Length <= 80, title);
        Assert.StartsWith($"Restore 12 changes made by {Grant} (transition on x", title, StringComparison.Ordinal);
        Assert.EndsWith("…)?", title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_caller_is_shortened_and_the_label_dropped()
    {
        var entry = new ChangeSetEntry(AgentRun with { Caller = new string('g', 120) }, 2);

        var title = RestoreProposals.Title(entry);

        Assert.True(title.Length <= 80, title);
        Assert.EndsWith("…?", title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_set_without_a_caller_is_named_by_its_actor()
    {
        var entry = new ChangeSetEntry(new ChangeSet(Guid.NewGuid(), ChangeActor.Sync, null, AgentRun.StartedAt), 4);

        Assert.Equal("Restore 4 changes made by sync?", RestoreProposals.Title(entry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3f1c2b8e-0000-4000-8000-000000000000")]
    [InlineData("restore/not-a-guid/x")]
    public void A_key_that_is_no_proposals_names_no_change_set(string? key) =>
        Assert.Null(RestoreProposals.ChangeSetOf(key));

    /// <summary>No tool restores directly (local ADR 0061 §5): no served tool class is handed
    /// the restore port, so none can reach it.</summary>
    [Fact]
    public void No_tool_class_takes_the_restore_port()
    {
        var takers = BacklogMcpTools.Groups
            .Select(group => group.ToolType)
            .Distinct()
            .Where(type => type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Any(parameter => parameter.ParameterType == typeof(IChangeRestore)))
            .ToList();

        Assert.Empty(takers);
    }

    /// <summary>The change journal, in memory: answers what a test seeds and records every
    /// query it was asked.</summary>
    private sealed class FakeJournal(params ChangeSetEntry[] entries) : IChangeJournal
    {
        public List<ChangeSetQuery> Queries { get; } = [];

        public Task<IReadOnlyList<ChangeSetEntry>> ListAsync(string? sessionId = null, int limit = 200, CancellationToken cancellationToken = default) =>
            QueryAsync(new ChangeSetQuery(SessionId: sessionId, Limit: limit), cancellationToken);

        public Task<IReadOnlyList<ChangeSetEntry>> QueryAsync(ChangeSetQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<ChangeSetEntry>>([.. entries.Take(query.Limit)]);
        }

        public Task<ChangeSetEntry?> FindAsync(Guid changeSetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(entries.FirstOrDefault(entry => entry.ChangeSet.Id == changeSetId));

        public Task<IReadOnlyList<ChangedDocument>> ReadAsync(Guid changeSetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChangedDocument>>([]);
    }
}
