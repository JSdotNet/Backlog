using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The waiting questions in <c>backlog.db</c> (local ADR 0041): a question comes back as it
/// went in, a second register for the same session and tool use returns the first, and a
/// transition is written only when it succeeds.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteWaitingQuestionStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly RelayQuestion Pick = new(
        "Which database?",
        "Database",
        [new RelayOption("SQLite", "A local file"), new RelayOption("Postgres")]);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-waiting-questions", Guid.NewGuid().ToString("n"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqliteWaitingQuestionStore Store() => new(_root);

    private static WaitingQuestion Dialog(string toolUse = "toolu_1", bool away = false) =>
        WaitingQuestions.Ask("session-1", toolUse, [Pick, new RelayQuestion("Tags?", null, [new RelayOption("a"), new RelayOption("b")], true)], away, TimeSpan.FromMinutes(15), Noon);

    [Fact]
    public async Task A_registered_question_comes_back_whole()
    {
        var store = Store();
        var question = Dialog();

        Assert.Same(question, await store.RegisterAsync(question, Ct));

        Assert.Equal(question, await store.GetAsync(question.Id, Ct));
        Assert.Equal([question], await store.ListAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Every_field_survives_a_round_trip()
    {
        var store = Store();
        var question = Dialog(away: true);
        await store.RegisterAsync(question, Ct);

        var notified = WaitingQuestions.Polled(WaitingQuestions.Notify(question, [Guid.NewGuid(), Guid.NewGuid()]).Question!, Noon);
        var answered = WaitingQuestions.Answer(notified, notified.NotificationIds[0], RelayAnswer.Open("MySQL"), Noon.AddMinutes(5)).Question!;
        answered = WaitingQuestions.Answer(answered, answered.NotificationIds[1], new RelayAnswer(["a", "b"]), Noon.AddMinutes(5)).Question!;

        await store.ChangeAsync(question.Id, _ => WaitingQuestionOutcome.Done(answered), Ct);

        var read = (await store.GetAsync(question.Id, Ct))!;
        Assert.Equal(answered, read);
        Assert.Equal(AnswerDeliveryState.Pending, read.Delivery);
    }

    [Fact]
    public async Task Registering_the_same_session_and_tool_use_twice_returns_the_first()
    {
        var store = Store();
        var first = await store.RegisterAsync(Dialog(), Ct);

        Assert.Equal(first, await store.RegisterAsync(Dialog(), Ct));
        Assert.NotEqual(first.Id, (await store.RegisterAsync(Dialog("toolu_2"), Ct)).Id);
        Assert.Equal(2, (await store.ListAsync(cancellationToken: Ct)).Count);
    }

    [Fact]
    public async Task Prose_questions_are_each_their_own()
    {
        var store = Store();

        await store.RegisterAsync(WaitingQuestions.Ask("session-1", null, [RelayQuestion.Prose("A?")], false, TimeSpan.FromMinutes(1), Noon), Ct);
        await store.RegisterAsync(WaitingQuestions.Ask("session-1", null, [RelayQuestion.Prose("A?")], false, TimeSpan.FromMinutes(1), Noon), Ct);

        Assert.Equal(2, (await store.ListAsync(cancellationToken: Ct)).Count);
    }

    [Fact]
    public async Task Open_only_lists_InSession_and_Notified()
    {
        var store = Store();
        var open = await store.RegisterAsync(Dialog("toolu_1"), Ct);
        var closed = await store.RegisterAsync(Dialog("toolu_2"), Ct);

        await store.ChangeAsync(closed.Id, question => WaitingQuestions.Close(question, WaitingQuestionCloseReason.TimedOut, null, Noon), Ct);

        Assert.Equal([open.Id], (await store.ListAsync(openOnly: true, Ct)).Select(question => question.Id));
    }

    [Fact]
    public async Task A_refused_transition_writes_nothing()
    {
        var store = Store();
        var question = await store.RegisterAsync(Dialog(), Ct);

        var outcome = await store.ChangeAsync(question.Id, held => WaitingQuestions.Notify(held, [Guid.NewGuid()]), Ct);

        Assert.False(outcome.Succeeded);
        Assert.Equal(question, await store.GetAsync(question.Id, Ct));
        Assert.Null((await store.ChangeAsync(Guid.NewGuid(), held => WaitingQuestionOutcome.Done(held), Ct)).Question);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
