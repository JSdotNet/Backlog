using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.Tests;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// A Waiting row's "Asks" strip (<c>.devbook/domain/sessions/requirements.md#answer-a-sessions-question-from-elsewhere</c>):
/// the open question the session waits on, read from the question relay's waiting
/// questions, under the row's facts, with an Answer that opens that question's
/// notification in the notification centre. A dialog of several questions shows the
/// first still open and how many more are.
/// </summary>
public sealed class SessionsPaneAsksTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan NotifyAfter = TimeSpan.FromMinutes(15);

    private static readonly RelayQuestion Branch = new(
        "Which landing branch: main or plan/schedules?",
        "Branch",
        [new RelayOption("main"), new RelayOption("plan/schedules")]);

    private static readonly RelayQuestion Tests = new(
        "Run the UI tests too?",
        "Tests",
        [new RelayOption("Yes"), new RelayOption("No")]);

    private static readonly RelayQuestion Draft = new(
        "Open it as a draft?",
        "Draft",
        [new RelayOption("Yes"), new RelayOption("No")]);

    [Fact]
    public void A_waiting_row_shows_what_its_session_asks_under_its_facts()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Notified(Ask("waiting-1", Branch)));
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.OnAnswerQuestion, (Guid _) => { }));

        pane.WaitForAssertion(() =>
        {
            var row = pane.Find("[data-testid='sessions-row']");
            var asks = row.QuerySelector("[data-testid='sessions-asks']")!;

            Assert.Equal(Branch.Question, asks.QuerySelector("[data-testid='sessions-asks-question']")!.TextContent.Trim());
            Assert.StartsWith("Asks", asks.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Null(asks.QuerySelector("[data-testid='sessions-asks-more']"));

            // Under the facts: the strip comes after the facts line in the row.
            var lines = row.Children.Select(child => child.GetAttribute("data-testid") ?? child.ClassName).ToList();
            Assert.True(
                lines.IndexOf("sessions-asks") > lines.FindIndex(line => line?.Contains("sessions-row__facts", StringComparison.Ordinal) == true),
                $"Expected the Asks strip after the facts, got: {string.Join(", ", lines)}");
        });
    }

    [Fact]
    public void Answer_opens_the_questions_notification_in_the_notification_centre()
    {
        var questions = new HeldWaitingQuestions();
        var question = questions.Add(Notified(Ask("waiting-1", Branch)));
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));
        var opened = new List<Guid>();

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.OnAnswerQuestion, (Guid id) => opened.Add(id)));

        pane.WaitForElement("[data-testid='sessions-asks-answer']");

        var answer = pane.Find("[data-testid='sessions-asks-answer']");

        // A control of its own: never inside the row button, where a control is
        // markup no browser agrees on.
        Assert.Null(answer.Closest("[data-testid='sessions-row']"));
        Assert.Equal("Answer", answer.TextContent.Trim());

        answer.Click();

        Assert.Equal([question.NotificationIds[0]], opened);
    }

    [Fact]
    public void Pressing_Answer_does_not_select_the_row()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Notified(Ask("waiting-1", Branch)));
        using var context = Context(
            questions,
            Session("running-1", AgentSessionState.Running, lastActivity: Noon.AddMinutes(-1)),
            Session("waiting-1", AgentSessionState.Waiting, lastActivity: Noon.AddMinutes(-5)));

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.OnAnswerQuestion, (Guid _) => { }));

        pane.WaitForElement("[data-testid='sessions-asks-answer']");
        var before = pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key");

        pane.Find("[data-testid='sessions-asks-answer']").Click();

        Assert.Equal(before, pane.Find("[data-testid='sessions-detail']").GetAttribute("data-row-key"));
    }

    [Fact]
    public void A_dialog_of_several_questions_shows_the_first_and_how_many_more()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Notified(Ask("waiting-1", Branch, Tests, Draft)));
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(Branch.Question, pane.Find("[data-testid='sessions-asks-question']").TextContent.Trim());
            Assert.Equal("+2 more", pane.Find("[data-testid='sessions-asks-more']").TextContent.Trim());
        });
    }

    [Fact]
    public void A_dialog_answered_in_part_shows_its_first_open_question_and_opens_that_ones_notification()
    {
        var questions = new HeldWaitingQuestions();
        var notified = Notified(Ask("waiting-1", Branch, Tests, Draft));
        var partly = questions.Add(WaitingQuestions.Answer(notified, notified.NotificationIds[0], new RelayAnswer(["main"]), Noon).Question!);
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));
        var opened = new List<Guid>();

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.OnAnswerQuestion, (Guid id) => opened.Add(id)));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(Tests.Question, pane.Find("[data-testid='sessions-asks-question']").TextContent.Trim());
            Assert.Equal("+1 more", pane.Find("[data-testid='sessions-asks-more']").TextContent.Trim());
        });

        pane.Find("[data-testid='sessions-asks-answer']").Click();

        Assert.Equal([partly.NotificationIds[1]], opened);
    }

    [Fact]
    public void A_question_still_in_the_session_shows_without_an_Answer()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Ask("waiting-1", Branch));
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.OnAnswerQuestion, (Guid _) => { }));

        pane.WaitForElement("[data-testid='sessions-asks']");

        Assert.Empty(pane.FindAll("[data-testid='sessions-asks-answer']"));
    }

    [Fact]
    public void Without_a_way_to_open_the_centre_the_strip_has_no_Answer()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Notified(Ask("waiting-1", Branch)));
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>();

        pane.WaitForElement("[data-testid='sessions-asks']");

        Assert.Empty(pane.FindAll("[data-testid='sessions-asks-answer']"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_surface_offers_Answer_only_while_the_window_offers_the_notification_centre(bool offered)
    {
        var questions = new HeldWaitingQuestions();
        var question = questions.Add(Notified(Ask("waiting-1", Branch)));
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));
        var opened = new List<Guid>();
        context.Services.AddSingleton<ISurfaceCatalog>(new Catalog());
        context.Services.AddSingleton(new SessionsSurfaceWiring
        {
            AnswerQuestion = id =>
            {
                opened.Add(id);
                return Task.CompletedTask;
            },
            AnswerQuestionOffered = () => offered,
        });

        var surface = context.Render<SessionsSurface>();

        surface.WaitForElement("[data-testid='sessions-asks']");

        if (!offered)
        {
            Assert.Empty(surface.FindAll("[data-testid='sessions-asks-answer']"));
            return;
        }

        surface.Find("[data-testid='sessions-asks-answer']").Click();
        Assert.Equal([question.NotificationIds[0]], opened);
    }

    [Fact]
    public void Answer_is_named_by_its_question_for_a_screen_reader()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Notified(Ask("waiting-1", Branch)));
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(p => p.OnAnswerQuestion, (Guid _) => { }));

        var answer = pane.WaitForElement("[data-testid='sessions-asks-answer']");

        Assert.Equal($"Answer: {Branch.Question}", answer.GetAttribute("aria-label"));
    }

    [Fact]
    public void Only_a_row_that_reads_Waiting_shows_a_strip()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Notified(Ask("running-1", Branch)));
        questions.Add(Notified(Ask("waiting-1", Tests)));
        using var context = Context(
            questions,
            Session("running-1", AgentSessionState.Running, lastActivity: Noon.AddMinutes(-1)),
            Session("waiting-1", AgentSessionState.Waiting, lastActivity: Noon.AddMinutes(-5)),
            Session("quiet-1", AgentSessionState.Waiting, lastActivity: Noon.AddMinutes(-9)));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Equal(3, pane.FindAll("[data-testid='sessions-row']").Count));

        var strips = pane.FindAll("[data-testid='sessions-asks-question']");
        Assert.Equal([Tests.Question], strips.Select(strip => strip.TextContent.Trim()));
    }

    [Fact]
    public void A_closed_or_answered_question_shows_no_strip()
    {
        var questions = new HeldWaitingQuestions();
        var notified = Notified(Ask("waiting-1", Branch));
        questions.Add(WaitingQuestions.Answer(notified, notified.NotificationIds[0], new RelayAnswer(["main"]), Noon).Question!);
        using var context = Context(questions, Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='sessions-row']")));
        Assert.Empty(pane.FindAll("[data-testid='sessions-asks']"));
    }

    [Fact]
    public void A_host_with_no_question_relay_lists_its_sessions_without_strips()
    {
        using var context = Context(questions: null, Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='sessions-row']")));
        Assert.Empty(pane.FindAll("[data-testid='sessions-asks']"));
    }

    [Fact]
    public void An_unreadable_question_store_leaves_the_list_as_it_was()
    {
        using var context = Context(new UnreadableQuestions(), Session("waiting-1", AgentSessionState.Waiting));

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='sessions-row']")));
        Assert.Empty(pane.FindAll("[data-testid='sessions-asks']"));
        Assert.Empty(pane.FindAll("[data-testid='sessions-unreadable']"));
    }

    [Fact]
    public async Task A_session_with_two_open_questions_shows_the_one_it_asked_first()
    {
        var questions = new HeldWaitingQuestions();
        questions.Add(Notified(WaitingQuestions.Ask("waiting-1", "toolu_2", [Tests], isAway: true, NotifyAfter, Noon.AddMinutes(-2))));
        questions.Add(Notified(WaitingQuestions.Ask("waiting-1", "toolu_1", [Branch], isAway: true, NotifyAfter, Noon.AddMinutes(-8))));

        var ask = SessionAsk.BySession(await questions.ListAsync(openOnly: true, TestContext.Current.CancellationToken))["waiting-1"];

        Assert.Equal(Branch.Question, ask.Question);
        Assert.Equal(0, ask.More);
    }

    private static WaitingQuestion Ask(string sessionId, params RelayQuestion[] questions) =>
        WaitingQuestions.Ask(sessionId, $"toolu_{sessionId}", questions, isAway: false, NotifyAfter, Noon.AddMinutes(-20));

    private static WaitingQuestion Notified(WaitingQuestion question) =>
        WaitingQuestions.Notify(question, [.. question.Questions.Select(_ => Guid.NewGuid())]).Question!;

    private static AgentSession Session(string id, AgentSessionState state, DateTimeOffset? lastActivity = null) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: $"Session {id}",
            WorkingFolder: $@"D:\Repos\Backlog\.claude\worktrees\{id}",
            Repository: "JSdotNet/Backlog",
            Branch: "main",
            StartedAt: Noon.AddHours(-1),
            LastActivityAt: lastActivity ?? Noon.AddMinutes(-3),
            State: state,
            TurnCount: 4,
            Origin: AgentSessionOrigin.Local);

    private static BunitContext Context(IWaitingQuestionStore? questions, params AgentSession[] sessions)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IAgentSessionSource>(new Sessions(sessions));
        context.Services.AddSingleton<IDeliveryRunSource>(new SessionsPaneTests.StubRunSource([], []));
        if (questions is not null) context.Services.AddSingleton(questions);

        return context;
    }

    private sealed class Sessions(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }

    /// <summary>A catalog that offers nothing: the pull requests chip stays off, and the
    /// wiring alone decides the Answer.</summary>
    private sealed class Catalog : ISurfaceCatalog
    {
        public IReadOnlyList<ShellSurface> Surfaces => [];

        public bool TryGet(string surfaceId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ShellSurface? surface)
        {
            surface = null;
            return false;
        }
    }

    private sealed class UnreadableQuestions : IWaitingQuestionStore
    {
        public Task<WaitingQuestion> RegisterAsync(WaitingQuestion candidate, CancellationToken cancellationToken = default) =>
            throw new IOException("The database is locked.");

        public Task<WaitingQuestion?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new IOException("The database is locked.");

        public Task<IReadOnlyList<WaitingQuestion>> ListAsync(bool openOnly = false, CancellationToken cancellationToken = default) =>
            throw new IOException("The database is locked.");

        public Task<WaitingQuestionOutcome> ChangeAsync(
            Guid id,
            Func<WaitingQuestion, WaitingQuestionOutcome> transition,
            CancellationToken cancellationToken = default) =>
            throw new IOException("The database is locked.");
    }
}
