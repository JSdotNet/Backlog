using Backlog.Desktop.UI.Changes;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Changes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Changes view over the journal (local ADR 0061): it lists what the journal
/// answers, narrows to a session when asked and says so, reads an opened row's
/// documents, and says when the host records no journal.
/// </summary>
public sealed class ChangesSurfaceTests
{
    private static readonly ChangeSet AgentRun = new(Guid.NewGuid(), ChangeActor.Agent, "transition", new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero))
    {
        Caller = "claude-code",
        SessionId = "session-9",
    };

    [Fact]
    public void It_lists_every_change_set_the_journal_holds()
    {
        var journal = new RecordingJournal();
        using var context = Context(journal);

        var view = context.Render<ChangesSurface>();

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set']")));
        Assert.Null(journal.Sessions.Single());
        Assert.Empty(view.FindAll("[data-testid='changes-session-filter']"));
    }

    [Fact]
    public void A_session_query_narrows_it_and_show_all_widens_it_again()
    {
        var journal = new RecordingJournal();
        using var context = Context(journal);

        var view = context.Render<ChangesSurface>(parameters => parameters
            .Add(p => p.Query, new Dictionary<string, string>(StringComparer.Ordinal) { [ChangesSurfaceQuery.SessionKey] = "session-9" }));

        view.WaitForAssertion(() => Assert.Contains("session-9", view.Find("[data-testid='changes-session-filter']").TextContent, StringComparison.Ordinal));
        Assert.Equal("session-9", journal.Sessions.Last());

        view.Find("[data-testid='changes-show-all']").Click();

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-testid='changes-session-filter']")));
        Assert.Null(journal.Sessions.Last());
    }

    [Fact]
    public void Opening_a_row_reads_its_documents_once()
    {
        var journal = new RecordingJournal();
        using var context = Context(journal);

        var view = context.Render<ChangesSurface>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set-toggle']")));

        view.Find("[data-testid='change-set-toggle']").Click();

        view.WaitForAssertion(() => Assert.Equal("Plan the trip", view.Find("[data-testid='changed-document-title']").TextContent));
        Assert.Equal([AgentRun.Id], journal.Reads);

        view.Find("[data-testid='change-set-toggle']").Click();
        view.Find("[data-testid='change-set-toggle']").Click();

        Assert.Equal([AgentRun.Id], journal.Reads);
    }

    /// <summary>A row open across Refresh is read again, never left saying it is
    /// reading what it changed.</summary>
    [Fact]
    public void Refresh_reads_an_open_rows_documents_again()
    {
        var journal = new RecordingJournal();
        using var context = Context(journal);

        var view = context.Render<ChangesSurface>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set-toggle']")));
        view.Find("[data-testid='change-set-toggle']").Click();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-testid='changed-document']")));

        view.Find("[data-testid='changes-refresh']").Click();

        view.WaitForAssertion(() =>
        {
            Assert.Equal([AgentRun.Id, AgentRun.Id], journal.Reads);
            Assert.NotEmpty(view.FindAll("[data-testid='changed-document']"));
            Assert.Empty(view.FindAll("[data-testid='change-set-reading']"));
        });
    }

    /// <summary>A re-render with the same query is no new jump: the list is read once.</summary>
    [Fact]
    public void A_re_render_with_no_query_does_not_read_the_journal_again()
    {
        var journal = new RecordingJournal();
        using var context = Context(journal);

        var view = context.Render<ChangesSurface>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set']")));

        view.Render();
        view.Render();

        Assert.Single(journal.Sessions);
    }

    [Fact]
    public void A_host_with_no_journal_says_so()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<ChangesSurface>();

        Assert.Contains("no change journal", view.Find("[data-testid='changes-unavailable']").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("[data-testid='changes-refresh']"));
    }

    [Fact]
    public void A_journal_that_fails_to_read_says_so()
    {
        using var context = Context(new RecordingJournal { Fails = true });

        var view = context.Render<ChangesSurface>();

        view.WaitForAssertion(() => Assert.Contains("could not be read", view.Find("[data-testid='changes-error']").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_restore_an_opened_row_offers_none()
    {
        using var context = Context(new RecordingJournal());

        var view = context.Render<ChangesSurface>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set-toggle']")));
        view.Find("[data-testid='change-set-toggle']").Click();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-testid='changed-document']")));

        Assert.Empty(view.FindAll("[data-testid='change-set-restore']"));
    }

    [Fact]
    public void Restore_plans_in_the_row_runs_with_the_choices_and_shows_the_row_restored()
    {
        var journal = new RecordingJournal();
        var restore = new RecordingRestore(journal);
        using var context = Context(journal);
        context.Services.AddSingleton<IChangeRestore>(restore);

        var view = context.Render<ChangesSurface>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set-toggle']")));
        view.Find("[data-testid='change-set-toggle']").Click();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set-restore']")));

        view.Find("[data-testid='change-set-restore']").Click();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-restore-conflict']")));
        view.Find("[data-testid='change-restore-anyway']").Click();
        view.Find("[data-testid='change-restore-confirm']").Click();

        view.WaitForAssertion(() =>
        {
            Assert.Equal([new DocumentKey("task", "t-2")], restore.Overrides);
            Assert.Contains("Restore of transition: 2 documents restored", view.Find("[data-testid='changes-restored']").TextContent, StringComparison.Ordinal);
            Assert.Single(view.FindAll("[data-testid='change-set-restored']"));
            Assert.Empty(view.FindAll("[data-testid='change-restore']"));
        });
    }

    [Fact]
    public void Cancel_closes_the_panel_and_restores_nothing()
    {
        var journal = new RecordingJournal();
        var restore = new RecordingRestore(journal);
        using var context = Context(journal);
        context.Services.AddSingleton<IChangeRestore>(restore);

        var view = context.Render<ChangesSurface>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set-toggle']")));
        view.Find("[data-testid='change-set-toggle']").Click();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-set-restore']")));
        view.Find("[data-testid='change-set-restore']").Click();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='change-restore']")));

        view.Find("[data-testid='change-restore-cancel']").Click();

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-testid='change-restore']")));
        Assert.Null(restore.Overrides);
    }

    /// <summary>The jump a restore proposal answered Restore makes (local ADR 0061 §5): the
    /// row opens with its confirmation panel, and nothing is restored until the person
    /// presses Restore there.</summary>
    [Fact]
    public void A_restore_query_opens_the_change_sets_confirmation_panel_and_restores_nothing()
    {
        var journal = new RecordingJournal();
        var restore = new RecordingRestore(journal);
        using var context = Context(journal);
        context.Services.AddSingleton<IChangeRestore>(restore);

        var view = context.Render<ChangesSurface>(parameters => parameters
            .Add(p => p.Query, new Dictionary<string, string>(StringComparer.Ordinal) { [ChangesSurfaceQuery.RestoreKey] = AgentRun.Id.ToString("D") }));

        view.WaitForAssertion(() =>
        {
            Assert.Single(view.FindAll("[data-testid='change-restore']"));
            Assert.NotEmpty(view.FindAll("[data-testid='changed-document']"));
        });
        Assert.Equal([AgentRun.Id], journal.Reads);
        Assert.Null(restore.Overrides);
        Assert.Null(journal.UndoneBy);

        view.Find("[data-testid='change-restore-cancel']").Click();

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-testid='change-restore']")));
        Assert.Null(restore.Overrides);
    }

    [Fact]
    public void A_restore_query_for_a_change_set_already_restored_says_so_and_opens_no_panel()
    {
        var journal = new RecordingJournal { UndoneBy = Guid.NewGuid() };
        using var context = Context(journal);
        context.Services.AddSingleton<IChangeRestore>(new RecordingRestore(journal));

        var view = context.Render<ChangesSurface>(parameters => parameters
            .Add(p => p.Query, new Dictionary<string, string>(StringComparer.Ordinal) { [ChangesSurfaceQuery.RestoreKey] = AgentRun.Id.ToString("D") }));

        view.WaitForAssertion(() => Assert.Contains("already restored", view.Find("[data-testid='changes-restored']").TextContent, StringComparison.Ordinal));
        Assert.Empty(view.FindAll("[data-testid='change-restore']"));
    }

    [Fact]
    public void A_restore_query_for_a_change_set_not_listed_says_so()
    {
        var journal = new RecordingJournal();
        using var context = Context(journal);
        context.Services.AddSingleton<IChangeRestore>(new RecordingRestore(journal));

        var view = context.Render<ChangesSurface>(parameters => parameters
            .Add(p => p.Query, new Dictionary<string, string>(StringComparer.Ordinal) { [ChangesSurfaceQuery.RestoreKey] = Guid.NewGuid().ToString("D") }));

        view.WaitForAssertion(() => Assert.Contains("not among", view.Find("[data-testid='changes-error']").TextContent, StringComparison.Ordinal));
        Assert.Empty(view.FindAll("[data-testid='change-restore']"));
        Assert.Empty(journal.Reads);
    }

    [Fact]
    public void The_registration_makes_it_a_view_that_accepts_a_session()
    {
        var services = new ServiceCollection();

        services.AddChangesSurface();
        services.AddChangesSurface();

        var surface = Assert.Single(services, descriptor => descriptor.ImplementationInstance is ShellSurface).ImplementationInstance as ShellSurface;
        Assert.Equal(ChangesSurfaceQuery.SurfaceId, surface!.Id);
        Assert.Equal(ShellSurfaceGroup.View, surface.Group);
        Assert.Equal(typeof(ChangesSurface), surface.Component);
        Assert.Equal([ChangesSurfaceQuery.SessionKey, ChangesSurfaceQuery.RestoreKey], surface.QueryKeys);
    }

    private static BunitContext Context(IChangeJournal journal)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(journal);
        return context;
    }

    private sealed class RecordingJournal : IChangeJournal
    {
        public List<string?> Sessions { get; } = [];

        public List<Guid> Reads { get; } = [];

        public bool Fails { get; init; }

        public Guid? UndoneBy { get; set; }

        public Task<IReadOnlyList<ChangeSetEntry>> ListAsync(string? sessionId = null, int limit = 200, CancellationToken cancellationToken = default)
        {
            Sessions.Add(sessionId);
            return Fails
                ? Task.FromException<IReadOnlyList<ChangeSetEntry>>(new InvalidOperationException("locked"))
                : Task.FromResult<IReadOnlyList<ChangeSetEntry>>([new ChangeSetEntry(AgentRun with { UndoneBy = UndoneBy }, 1)]);
        }

        public Task<IReadOnlyList<ChangeSetEntry>> QueryAsync(ChangeSetQuery query, CancellationToken cancellationToken = default) =>
            ListAsync(query.SessionId, query.Limit, cancellationToken);

        public Task<ChangeSetEntry?> FindAsync(Guid changeSetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChangeSetEntry?>(changeSetId == AgentRun.Id ? new ChangeSetEntry(AgentRun with { UndoneBy = UndoneBy }, 1) : null);

        public Task<IReadOnlyList<ChangedDocument>> ReadAsync(Guid changeSetId, CancellationToken cancellationToken = default)
        {
            Reads.Add(changeSetId);
            return Task.FromResult<IReadOnlyList<ChangedDocument>>(
                [new ChangedDocument("task", "t-1", "Plan the trip", "# Plan the trip\n`task` `*medium` `!ready`\n", "# Plan the trip\n`task` `*medium` `!done`\n")]);
        }
    }

    /// <summary>A restore that plans one clean document and one conflict, and marks the
    /// journal's change set restored when it runs.</summary>
    private sealed class RecordingRestore(RecordingJournal journal) : IChangeRestore
    {
        public IReadOnlySet<DocumentKey>? Overrides { get; private set; }

        public Task<RestorePlan?> PlanAsync(Guid changeSetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RestorePlan?>(new RestorePlan(
                changeSetId,
                AgentRun.Label,
                journal.UndoneBy,
                [
                    new(new DocumentKey("task", "t-1"), "Plan the trip", RestoreAction.Revert, Conflict: false),
                    new(new DocumentKey("task", "t-2"), "Pack", RestoreAction.Revert, Conflict: true),
                ]));

        public Task<RestoreOutcome> RestoreAsync(Guid changeSetId, IReadOnlySet<DocumentKey> restoreAnyway, CancellationToken cancellationToken = default)
        {
            Overrides = restoreAnyway;
            var restore = Guid.NewGuid();
            journal.UndoneBy = restore;
            return Task.FromResult(new RestoreOutcome(restore, RestorePlan.RestoreLabelFor(AgentRun.Label), 1 + restoreAnyway.Count, 1 - restoreAnyway.Count));
        }
    }
}
