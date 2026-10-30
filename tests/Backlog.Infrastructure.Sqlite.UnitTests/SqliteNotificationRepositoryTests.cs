using Backlog.Infrastructure.Sqlite.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The stored notifications: one row per notification in <c>notifications</c>, the whole
/// notification as one document. What goes in comes back out unchanged — its answer spec, its
/// accepted answer and whether that answer still waits for its handler — the open and
/// awaiting-delivery reads see only their rows, and a row that will not parse is skipped.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteNotificationRepositoryTests : IDisposable
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 41, 0, TimeSpan.Zero);
    private static readonly NotificationSubject Session = new("sessions", "session", "sess-91");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notification-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteNotificationRepository _repository;

    public SqliteNotificationRepositoryTests() => _repository = new SqliteNotificationRepository(_dir);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Notification Question(DateTimeOffset? at = null) => Notification.Publish(
        Guid.NewGuid(),
        Owner,
        new NotificationDraft(
            new NotificationSource("sessions", "run-58"),
            Session,
            NotificationKind.Question,
            NotificationSeverity.Critical,
            "Run the migration against staging?",
            "0042_add_device_kind",
            "| table | rows |\n|---|---|\n| tasks | 12k |",
            new AnswerSpec(
                [new AnswerChoice("yes", "Yes, run it", AnswerChoiceStyle.Primary), new AnswerChoice("no", "Not now", AnswerChoiceStyle.Destructive)],
                Open: new OpenAnswer("Or say why", 300),
                AnswerOn: AnswerOn.Owner),
            "backlog://sessions/sess-91",
            IsPrivate: true),
        at ?? Now).Value;

    private static Notification Alert() => Notification.Publish(
        Guid.NewGuid(),
        Owner,
        new NotificationDraft(new NotificationSource("devbook", "remark-1"), null, NotificationKind.Alert, NotificationSeverity.Info, "Remark left", "", ""),
        Now).Value;

    private static NotificationReply Reply(Notification question, string choice) =>
        new(Guid.NewGuid(), question.Id, Owner, Owner, [choice], null, Now);

    [Fact]
    public async Task A_stored_notification_reads_back_unchanged()
    {
        var question = Question();

        await _repository.SaveAsync(question, Cancellation);
        var read = await _repository.GetAsync(question.Id, Cancellation);

        Assert.NotNull(read);
        Assert.Equal(question.OwnerInstanceId, read.OwnerInstanceId);
        Assert.Equal(question.Source, read.Source);
        Assert.Equal(question.Subject, read.Subject);
        Assert.Equal(question.Kind, read.Kind);
        Assert.Equal(question.Severity, read.Severity);
        Assert.Equal(question.Title, read.Title);
        Assert.Equal(question.Summary, read.Summary);
        Assert.Equal(question.BodyMarkdown, read.BodyMarkdown);
        Assert.Equal(question.AnswerSpec!.Choices, read.AnswerSpec!.Choices);
        Assert.Equal(question.AnswerSpec.Open, read.AnswerSpec.Open);
        Assert.Equal(AnswerOn.Owner, read.AnswerSpec.AnswerOn);
        Assert.Equal(question.DeepLink, read.DeepLink);
        Assert.True(read.IsPrivate);
        Assert.Equal(question.CreatedAt, read.CreatedAt);
        Assert.Equal(NotificationState.Open, read.State);
        Assert.Null(read.Origin);
    }

    [Fact]
    public async Task A_stored_origin_reads_back_unchanged()
    {
        var origin = new NotificationOrigin("Claude Code", "JSdotNet/Backlog · Notification origin", "Ada", "Agent session", "https://example.test/s/1");
        var alert = Notification.Publish(
            Guid.NewGuid(),
            Owner,
            new NotificationDraft(new NotificationSource("sessions", "run-1"), null, NotificationKind.Alert, NotificationSeverity.Info, "Run done", "", "", Origin: origin),
            Now).Value;

        await _repository.SaveAsync(alert, Cancellation);

        Assert.Equal(origin, (await _repository.GetAsync(alert.Id, Cancellation))!.Origin);
    }

    [Fact]
    public async Task The_centre_read_holds_every_open_row_and_the_rows_written_since_newest_first()
    {
        var older = Question(Now.AddMinutes(-5));
        var newer = Question(Now);
        var acknowledged = Alert();
        acknowledged.Acknowledge(Owner, Now);

        await _repository.SaveAsync(older, Cancellation);
        await _repository.SaveAsync(newer, Cancellation);
        await _repository.SaveAsync(acknowledged, Cancellation);

        // Written moments ago, so a window from an hour back holds it and one from the
        // future does not; the open rows are listed whatever the window.
        var recent = await _repository.ListOpenOrSettledSinceAsync(DateTimeOffset.UtcNow.AddHours(-1), Cancellation);
        var future = await _repository.ListOpenOrSettledSinceAsync(DateTimeOffset.UtcNow.AddHours(1), Cancellation);

        Assert.Contains(acknowledged.Id, recent.Select(n => n.Id));
        Assert.Equal([newer.Id, older.Id], future.Select(n => n.Id));
    }

    [Fact]
    public async Task An_unknown_id_reads_as_null()
    {
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), Cancellation));
    }

    [Fact]
    public async Task An_accepted_answer_is_stored_and_waits_for_its_handler_until_marked_delivered()
    {
        var question = Question();
        Assert.True(question.Accept(Reply(question, "yes"), Owner, Now).IsSuccess);
        await _repository.SaveAsync(question, Cancellation);

        var waiting = Assert.Single(await _repository.ListAwaitingDeliveryAsync(Cancellation));
        Assert.Equal(question.Id, waiting.Id);
        Assert.Equal(NotificationState.Answered, waiting.State);
        Assert.Equal(["yes"], waiting.Answer!.ChoiceIds);
        Assert.Equal(question.Answer!.ReplyId, waiting.Answer.ReplyId);

        waiting.MarkAnswerDelivered(Now);
        await _repository.SaveAsync(waiting, Cancellation);

        Assert.Empty(await _repository.ListAwaitingDeliveryAsync(Cancellation));
        Assert.Equal(Now, (await _repository.GetAsync(question.Id, Cancellation))!.AnswerDeliveredAt);
    }

    [Fact]
    public async Task Listing_open_notifications_leaves_settled_ones_out_oldest_first()
    {
        var later = Question(Now.AddMinutes(5));
        var earlier = Question(Now);
        var closed = Alert();
        closed.Close(Owner, "done", Now);

        await _repository.SaveAsync(later, Cancellation);
        await _repository.SaveAsync(closed, Cancellation);
        await _repository.SaveAsync(earlier, Cancellation);

        var open = await _repository.ListOpenAsync(Cancellation);

        Assert.Equal([earlier.Id, later.Id], open.Select(notification => notification.Id));
    }

    [Fact]
    public async Task A_choice_that_takes_text_reads_back_with_its_placeholder()
    {
        var question = Notification.Publish(
            Guid.NewGuid(),
            Owner,
            new NotificationDraft(
                new NotificationSource("sessions", "run-58"),
                Session,
                NotificationKind.Question,
                NotificationSeverity.Warning,
                "Approve the validation of run-58?",
                "Personal Validation",
                "Check the toast.",
                new AnswerSpec(
                [
                    new AnswerChoice("approve", "Approve", AnswerChoiceStyle.Primary),
                    new AnswerChoice("revise", "Revise", TakesText: true, Placeholder: "What should change?")
                ])),
            Now).Value;

        await _repository.SaveAsync(question, Cancellation);
        var read = await _repository.GetAsync(question.Id, Cancellation);

        Assert.Equal(
            [("approve", false, (string?)null), ("revise", true, "What should change?")],
            read!.AnswerSpec!.Choices.Select(choice => (choice.Id, choice.TakesText, choice.Placeholder)));
    }

    [Fact]
    public async Task A_document_written_before_choices_took_text_reads_as_taking_none()
    {
        const string Id = "11111111-1111-1111-1111-111111111111";
        const string Document = """{"version":1,"id":"11111111-1111-1111-1111-111111111111","ownerInstanceId":"22222222-2222-2222-2222-222222222222","source":{"context":"sessions","key":"run-1"},"kind":"Question","severity":"Info","title":"Approve?","summary":"","bodyMd":"","answer":{"choices":[{"id":"approve","label":"Approve","style":"Primary"},{"id":"reject","label":"Reject","style":"Destructive"}],"allowMultiple":false,"open":{"maxLength":2000},"answerOn":"AnyInstance"},"isPrivate":false,"createdAt":"2026-10-22T09:41:00+00:00","state":"Open"}""";

        await using (var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}"))
        {
            // Saving first creates the schema the raw row goes into.
            await _repository.SaveAsync(Question(), Cancellation);
            await connection.OpenAsync(Cancellation);
            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO notifications (id, state, awaits_delivery, created_at, updated_at, document) VALUES ('{Id}', 'Open', 0, '2026-01-01', '2026-01-01', $document);";
            command.Parameters.AddWithValue("$document", Document);
            await command.ExecuteNonQueryAsync(Cancellation);
        }

        var read = await _repository.GetAsync(Guid.Parse(Id), Cancellation);

        Assert.All(read!.AnswerSpec!.Choices, choice => Assert.False(choice.TakesText));
        Assert.All(read.AnswerSpec.Choices, choice => Assert.Null(choice.Placeholder));
    }

    [Fact]
    public async Task A_row_that_will_not_parse_is_skipped()
    {
        var question = Question();
        await _repository.SaveAsync(question, Cancellation);

        await using (var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}"))
        {
            await connection.OpenAsync(Cancellation);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO notifications (id, state, awaits_delivery, created_at, updated_at, document) VALUES ('bad', 'Open', 1, '2026-01-01', '2026-01-01', '{not json');";
            await command.ExecuteNonQueryAsync(Cancellation);
        }

        Assert.Equal([question.Id], (await _repository.ListOpenAsync(Cancellation)).Select(notification => notification.Id));
        Assert.Empty(await _repository.ListAwaitingDeliveryAsync(Cancellation));
    }

    [Theory]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","ownerInstanceId":"22222222-2222-2222-2222-222222222222","kind":"Alert","severity":"Info","title":"No source","summary":"","bodyMd":"","isPrivate":false,"createdAt":"2026-10-22T09:41:00+00:00","state":"Open"}""")]
    [InlineData("""{"version":1,"id":"11111111-1111-1111-1111-111111111111","ownerInstanceId":"22222222-2222-2222-2222-222222222222","source":{"context":"sessions","key":"run-1"},"kind":"Question","severity":"Info","title":"No spec","summary":"","bodyMd":"","isPrivate":false,"createdAt":"2026-10-22T09:41:00+00:00","state":"Open"}""")]
    [InlineData("""{"version":9,"id":"11111111-1111-1111-1111-111111111111","ownerInstanceId":"22222222-2222-2222-2222-222222222222","source":{"context":"sessions","key":"run-1"},"kind":"Alert","severity":"Info","title":"Later shape","summary":"","bodyMd":"","isPrivate":false,"createdAt":"2026-10-22T09:41:00+00:00","state":"Open"}""")]
    public async Task A_row_that_parses_but_does_not_read_as_a_notification_is_skipped(string document)
    {
        var question = Question();
        await _repository.SaveAsync(question, Cancellation);

        await using (var connection = new SqliteConnection($"Data Source={_repository.DatabasePath}"))
        {
            await connection.OpenAsync(Cancellation);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO notifications (id, state, awaits_delivery, created_at, updated_at, document) VALUES ('11111111-1111-1111-1111-111111111111', 'Open', 1, '2026-01-01', '2026-01-01', $document);";
            command.Parameters.AddWithValue("$document", document);
            await command.ExecuteNonQueryAsync(Cancellation);
        }

        Assert.Equal([question.Id], (await _repository.ListOpenAsync(Cancellation)).Select(notification => notification.Id));
        Assert.Empty(await _repository.ListAwaitingDeliveryAsync(Cancellation));
        Assert.Null(await _repository.GetAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), Cancellation));
    }

    [Fact]
    public async Task The_rooted_store_follows_the_storage_folder()
    {
        var root = _dir;
        var rooted = new RootedSqliteNotificationRepository(() => root);
        var question = Question();
        await rooted.SaveAsync(question, Cancellation);

        root = _dir + "-moved";
        try
        {
            Assert.Null(await rooted.GetAsync(question.Id, Cancellation));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
