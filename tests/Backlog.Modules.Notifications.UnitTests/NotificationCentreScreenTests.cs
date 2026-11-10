using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Services;
using Backlog.Modules.Notifications.UI;
using Backlog.SharedKernel;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// The centre's screens over the real owner ports and an in-memory store: the list grouped by
/// subject with each row's line, the pane that answers through the centre, and the shell's count of
/// open questions.
/// </summary>
public sealed class NotificationCentreScreenTests : IDisposable
{
    private static readonly NotificationSubject Task123 = new("tasks", "task", "T-123");

    private readonly InMemoryNotificationRepository _inner = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly OwnerNotifications _owner;
    private readonly NotificationCentre _centre;
    private readonly BunitContext _context = new();

    public NotificationCentreScreenTests()
    {
        var store = new ObservedNotificationRepository(_inner);
        _owner = new OwnerNotifications(store, new FixedDevice(Owner), new AnswerDelivery([], store, _time), _time);
        _centre = new NotificationCentre(store, _owner, new FixedDevice(Owner), _time);

        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<INotificationCentre>(_centre);
        _context.Services.AddSingleton<TimeProvider>(_time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _context.Dispose();
        _centre.Dispose();
        _owner.Dispose();
    }

    private async Task<Guid> PublishAsync(NotificationDraft draft)
    {
        var id = (await _owner.PublishAsync(draft, Ct)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));
        return id;
    }

    // --- The list ------------------------------------------------------------------------

    [Fact]
    public async Task The_list_groups_notifications_under_their_subject_with_the_open_question_group_first()
    {
        await PublishAsync(Alert());
        await PublishAsync(Question(subject: Task123));
        await PublishAsync(Question());

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var groups = list.FindAll("[data-testid='notification-group']");
        Assert.Equal(
            ["Session sess-91", "Task T-123", NotificationPresentation.NoSubjectLabel],
            groups.Select(group => group.QuerySelector("h3")!.TextContent));
    }

    [Fact]
    public async Task A_row_says_its_kind_who_raised_it_and_how_long_ago()
    {
        var id = await PublishAsync(Question());

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var row = list.Find($"[data-testid='notification-row-{id:N}']");
        Assert.Equal("Question", row.QuerySelector("[data-testid='notification-row-badge']")!.TextContent);
        Assert.Equal("Sessions · 1 min ago", row.QuerySelector("[data-testid='notification-row-line']")!.TextContent.Trim());
    }

    [Fact]
    public async Task An_answered_row_says_the_winning_answer_and_the_device_it_came_from()
    {
        var id = await PublishAsync(Question());
        await _centre.AnswerAsync(id, ["yes"], null, cancellationToken: Ct);

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var row = list.Find($"[data-testid='notification-row-{id:N}']");
        Assert.Equal("Answered", row.QuerySelector("[data-testid='notification-row-badge']")!.TextContent);
        Assert.Equal("Answered from this desktop", row.QuerySelector("[data-testid='notification-row-line']")!.TextContent.Trim());
        // The answer on a line of its own, not run into the lead.
        Assert.Equal("Yes, run it", row.QuerySelector("[data-testid='notification-row-answer']")!.TextContent);
    }

    [Fact]
    public async Task An_answered_row_shows_the_note_beside_the_winning_answer()
    {
        var spec = new AnswerSpec(
            [new AnswerChoice("yes", "Yes, run it"), new AnswerChoice("no", "Not now")],
            Note: new OpenAnswer());
        var id = await PublishAsync(Question(spec));
        await _centre.AnswerAsync(id, ["yes"], null, "After the backup", Ct);

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        var row = list.Find($"[data-testid='notification-row-{id:N}']");
        Assert.Equal(
            "Yes, run it, note: “After the backup”",
            row.QuerySelector("[data-testid='notification-row-answer']")!.TextContent);
    }

    [Fact]
    public async Task A_closed_row_says_why()
    {
        var id = await PublishAsync(Question());
        await _owner.CloseAsync(id, "Session archived", Ct);

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        Assert.Equal("Closed", list.Find("[data-testid='notification-row-line']").TextContent.Trim());
        Assert.Equal("Session archived", list.Find("[data-testid='notification-row-answer']").TextContent);
    }

    [Fact]
    public async Task Pressing_a_row_selects_it()
    {
        var id = await PublishAsync(Question());
        Guid? selected = null;

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.OnSelect, picked => selected = picked));

        list.Find($"[data-testid='notification-row-{id:N}']").Click();

        Assert.Equal(id, selected);
    }

    [Fact]
    public void An_empty_list_says_nothing_is_waiting()
    {
        var list = _context.Render<NotificationList>(p => p.Add(c => c.Notifications, []));

        Assert.NotNull(list.Find("[data-testid='notification-list-none']"));
    }

    // --- The pane ------------------------------------------------------------------------

    [Fact]
    public async Task The_pane_opens_on_the_open_question_and_answers_it_through_the_centre()
    {
        var id = await PublishAsync(Question());

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-choice-yes']")));
        Assert.Equal("1 waiting for an answer", pane.Find("[data-testid='notification-centre-subtitle']").TextContent);

        pane.Find("[data-testid='notification-card-choice-yes']").Click();

        // Settled now, and settled ones are hidden, but it stays in view so its answer reads.
        pane.WaitForAssertion(() => Assert.Equal(
            "Answered from this desktop · just now",
            pane.Find("[data-testid='notification-card-outcome']").TextContent));
        Assert.Equal("true", pane.Find("[data-testid='notification-card-settled-choice-yes']").GetAttribute("data-chosen"));
        Assert.Equal("true", pane.Find(Row(id)).GetAttribute("aria-current"));
        Assert.Equal(NotificationState.Answered, (await _inner.GetAsync(id, Ct))!.State);
    }

    [Fact]
    public async Task A_choice_that_takes_text_is_answered_from_the_pane_with_its_words()
    {
        var id = await PublishAsync(Question(new AnswerSpec(
        [
            new AnswerChoice("approve", "Approve", AnswerChoiceStyle.Primary),
            new AnswerChoice("revise", "Revise", TakesText: true, Placeholder: "What should change?")
        ])));

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-choice-revise']")));

        pane.Find("[data-testid='notification-card-choice-revise']").Click();
        Assert.Equal(NotificationState.Open, (await _inner.GetAsync(id, Ct))!.State);

        pane.Find("[data-testid='notification-card-choice-text'] textarea").Input("The toast overlaps the header");
        pane.Find("[data-testid='notification-card-choice-send']").Click();

        pane.WaitForAssertion(() => Assert.Equal(
            "The toast overlaps the header",
            pane.Find("[data-testid='notification-card-answer-text']").TextContent));
        Assert.Equal("true", pane.Find("[data-testid='notification-card-settled-choice-revise']").GetAttribute("data-chosen"));
        var answer = (await _inner.GetAsync(id, Ct))!.Answer!;
        Assert.Equal(["revise"], answer.ChoiceIds);
        Assert.Equal("The toast overlaps the header", answer.Text);
    }

    [Fact]
    public async Task A_refused_answer_shows_its_reason_and_keeps_the_controls()
    {
        var spec = new AnswerSpec([], Open: new OpenAnswer(MaxLength: 5));
        await PublishAsync(Question(spec));

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-open'] textarea")));

        pane.Find("[data-testid='notification-card-open'] textarea").Input("far too long");
        pane.Find("[data-testid='notification-card-send']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-error']")));
        Assert.NotNull(pane.Find("[data-testid='notification-card-send']"));
    }

    [Fact]
    public async Task An_alert_is_acknowledged_from_the_pane()
    {
        var id = await PublishAsync(Alert());

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-acknowledge']")));

        pane.Find("[data-testid='notification-card-acknowledge']").Click();

        pane.WaitForAssertion(() => Assert.Equal(NotificationState.Acknowledged, _inner.GetAsync(id, Ct).Result!.State));
    }

    [Fact]
    public async Task The_pane_opens_on_the_notification_it_is_focused_on()
    {
        var alert = await PublishAsync(Alert());
        await PublishAsync(Question());

        var pane = _context.Render<NotificationCentrePane>(p => p.Add(c => c.FocusNotificationId, alert));

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-card-acknowledge']")));
    }

    [Fact]
    public async Task The_pane_redraws_when_a_question_is_published_while_it_is_open()
    {
        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-list-none']")));

        await PublishAsync(Question());

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='notification-group']")));
    }

    // --- The origin ----------------------------------------------------------------------

    private static readonly NotificationOrigin ClaudeCode =
        new("Claude Code", "JSdotNet/Backlog · Notification origin", Via: "Agent session");

    [Fact]
    public async Task A_row_with_an_origin_says_its_system_and_place_instead_of_its_source()
    {
        var id = await PublishAsync(Question() with { Origin = ClaudeCode });

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        Assert.Equal(
            "Claude Code · JSdotNet/Backlog · Notification origin · 1 min ago",
            list.Find($"[data-testid='notification-row-{id:N}'] [data-testid='notification-row-line']").TextContent.Trim());
    }

    [Fact]
    public async Task The_pane_draws_the_origin_line_on_the_card()
    {
        await PublishAsync(Question() with { Origin = ClaudeCode });

        var pane = _context.Render<NotificationCentrePane>();

        pane.WaitForAssertion(() => Assert.Equal("Claude Code", pane.Find("[data-testid='notification-card-system']").TextContent));
        Assert.Equal("JSdotNet/Backlog · Notification origin", pane.Find("[data-testid='notification-card-place']").TextContent);
        // An agent's question is marked so, and "Agent session" is not said twice beside it.
        Assert.Equal("agent", pane.Find("[data-testid='notification-card-asker']").GetAttribute("data-asker"));
        Assert.Contains("AI agent", pane.Find("[data-testid='notification-card-asker']").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='notification-card-via']"));
    }

    [Fact]
    public async Task The_from_filter_narrows_the_list_to_one_system()
    {
        var fromClaude = await PublishAsync(Question() with { Origin = ClaudeCode });
        var fromBacklog = await PublishAsync(Alert() with { Origin = new NotificationOrigin("Backlog", "Pairing") });
        var unnamed = await PublishAsync(Question(subject: Task123));

        var pane = _context.Render<NotificationCentrePane>();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find($"[data-testid='notification-row-{unnamed:N}']")));

        var options = pane.FindAll("[data-testid='notification-centre-from'] option").Select(option => option.TextContent.Trim()).ToList();
        Assert.Equal(["From anywhere (3)", "From Backlog (1)", "From Claude Code (1)", "From Sessions (1)"], options);

        pane.Find("[data-testid='notification-centre-from'] select").Change("Backlog");

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(pane.Find($"[data-testid='notification-row-{fromBacklog:N}']"));
            Assert.Empty(pane.FindAll($"[data-testid='notification-row-{fromClaude:N}']"));
            Assert.Empty(pane.FindAll($"[data-testid='notification-row-{unnamed:N}']"));
        });
        Assert.NotNull(pane.Find("[data-testid='notification-card-acknowledge']"));

        pane.Find("[data-testid='notification-centre-from'] select").Change(string.Empty);

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find($"[data-testid='notification-row-{fromClaude:N}']")));
    }

    // --- The scope -----------------------------------------------------------------------

    /// <summary>Which repository each notification is about, as a host would answer: an alias,
    /// null for the Brain's; one left out was not worked out.</summary>
    private readonly Dictionary<Guid, string?> _repositoryOf = [];

    private Task<IReadOnlyDictionary<Guid, string?>> RepositoriesOf(IReadOnlyList<NotificationView> notifications, CancellationToken _) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string?>>(notifications
            .Where(n => _repositoryOf.ContainsKey(n.Id))
            .ToDictionary(n => n.Id, n => _repositoryOf[n.Id]));

    private IRenderedComponent<NotificationCentrePane> RenderScoped(params string[] scope) =>
        _context.Render<NotificationCentrePane>(p => p
            .Add(c => c.RepositoryScope, scope)
            .Add(c => c.RepositoriesOf, RepositoriesOf));

    private static string Row(Guid id) => $"[data-testid='notification-row-{id:N}']";

    private async Task<(Guid Backlog, Guid Fincent, Guid Brain)> PublishThreeRepositoriesAsync()
    {
        var backlog = await PublishAsync(Question() with { Origin = ClaudeCode });
        var fincent = await PublishAsync(Question(subject: Task123));
        var brain = await PublishAsync(Question(subject: new NotificationSubject("mcp", "ask", "k")) with { Origin = new NotificationOrigin("Backlog", "Pairing") });
        _repositoryOf[backlog] = "backlog";
        _repositoryOf[fincent] = "fincent";
        _repositoryOf[brain] = null;
        return (backlog, fincent, brain);
    }

    [Fact]
    public async Task One_scoped_repository_lists_only_what_resolves_to_it_and_says_how_many_wait_outside()
    {
        var (backlog, fincent, brain) = await PublishThreeRepositoriesAsync();

        var pane = RenderScoped("backlog");

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(backlog))));
        Assert.Empty(pane.FindAll(Row(fincent)));
        Assert.Empty(pane.FindAll(Row(brain)));
        Assert.Equal("1 waiting for an answer · 2 more outside this scope", pane.Find("[data-testid='notification-centre-subtitle']").TextContent);
    }

    [Fact]
    public async Task Several_scoped_repositories_list_their_union()
    {
        var (backlog, fincent, brain) = await PublishThreeRepositoriesAsync();

        var pane = RenderScoped("backlog", "fincent");

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(fincent))));
        Assert.NotNull(pane.Find(Row(backlog)));
        Assert.Empty(pane.FindAll(Row(brain)));
    }

    [Fact]
    public async Task The_brain_scoped_lists_what_resolves_to_no_repository()
    {
        var (backlog, _, brain) = await PublishThreeRepositoriesAsync();

        var pane = RenderScoped(BrainScope.Value);

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(brain))));
        Assert.Empty(pane.FindAll(Row(backlog)));
    }

    [Fact]
    public async Task A_notification_whose_repository_could_not_be_worked_out_is_listed_under_any_scope()
    {
        var unread = await PublishAsync(Question());

        var pane = RenderScoped("backlog");

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(unread))));
    }

    [Fact]
    public async Task A_scope_pressed_while_the_centre_is_open_redraws_the_list_and_picks_what_is_still_shown()
    {
        var (backlog, fincent, _) = await PublishThreeRepositoriesAsync();
        var pane = RenderScoped("backlog");
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(backlog))));

        pane.Render(p => p.Add(c => c.RepositoryScope, ["fincent"]));

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(pane.Find(Row(fincent)));
            Assert.Empty(pane.FindAll(Row(backlog)));
            Assert.Equal("true", pane.Find(Row(fincent)).GetAttribute("aria-current"));
        });
    }

    [Fact]
    public async Task A_scope_changed_in_place_on_the_list_the_host_handed_over_is_still_noticed()
    {
        var (backlog, fincent, _) = await PublishThreeRepositoriesAsync();
        // The window state hands over its own list and changes it in place when a chip is pressed.
        var scope = new List<string> { "backlog" };
        var pane = _context.Render<NotificationCentrePane>(p => p
            .Add(c => c.RepositoryScope, scope)
            .Add(c => c.RepositoriesOf, RepositoriesOf));
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(backlog))));

        scope[0] = "fincent";
        pane.Render(p => p.Add(c => c.RepositoryScope, scope));

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll(Row(backlog)));
            Assert.Equal("true", pane.Find(Row(fincent)).GetAttribute("aria-current"));
        });
    }

    [Fact]
    public async Task A_scope_changed_in_place_after_a_jump_set_the_scope_aside_narrows_the_list_again()
    {
        var (backlog, fincent, brain) = await PublishThreeRepositoriesAsync();
        var scope = new List<string> { "backlog" };
        var pane = _context.Render<NotificationCentrePane>(p => p
            .Add(c => c.RepositoryScope, scope)
            .Add(c => c.RepositoriesOf, RepositoriesOf));
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(backlog))));
        pane.Render(p => p.Add(c => c.FocusNotificationId, fincent));
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(brain))));

        scope[0] = BrainScope.Value;
        pane.Render(p => p.Add(c => c.RepositoryScope, scope));

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(pane.Find(Row(brain)));
            Assert.Empty(pane.FindAll(Row(backlog)));
            Assert.Empty(pane.FindAll(Row(fincent)));
        });
    }

    [Fact]
    public async Task A_from_the_scope_leaves_with_nothing_stays_chosen_in_the_select_counting_none()
    {
        var (backlog, fincent, _) = await PublishThreeRepositoriesAsync();
        _context.Services.AddSingleton<INotificationCentrePreferences>(
            new RememberedChoices(new NotificationCentreChoices(From: "Claude Code")));

        var pane = RenderScoped("fincent");

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-centre-clear-filters']")));
        Assert.Empty(pane.FindAll(Row(backlog)));
        Assert.Empty(pane.FindAll(Row(fincent)));
        var select = pane.Find("[data-testid='notification-centre-from'] select");
        Assert.Equal("Claude Code", select.GetAttribute("value"));
        Assert.Contains(
            "From Claude Code (0)",
            pane.FindAll("[data-testid='notification-centre-from'] option").Select(option => option.TextContent.Trim()));
    }

    /// <summary>A host answer the test releases by hand, one per load.</summary>
    private sealed record PendingAnswer(
        IReadOnlyList<NotificationView> Asked,
        CancellationToken Token,
        TaskCompletionSource<IReadOnlyDictionary<Guid, string?>> Answer);

    private (List<PendingAnswer> Calls, Func<IReadOnlyList<NotificationView>, CancellationToken, Task<IReadOnlyDictionary<Guid, string?>>> RepositoriesOf) HeldAnswers()
    {
        var calls = new List<PendingAnswer>();
        return (calls, (asked, token) =>
        {
            var answer = new TaskCompletionSource<IReadOnlyDictionary<Guid, string?>>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (calls) calls.Add(new PendingAnswer(asked, token, answer));
            return answer.Task;
        });
    }

    [Fact]
    public async Task A_newer_load_cancels_the_older_resolution_and_its_late_answer_never_overwrites_the_newer()
    {
        var first = await PublishAsync(Question() with { Origin = ClaudeCode });
        var (calls, held) = HeldAnswers();
        var pane = _context.Render<NotificationCentrePane>(p => p
            .Add(c => c.RepositoryScope, ["backlog"])
            .Add(c => c.RepositoriesOf, held));
        pane.WaitForAssertion(() => Assert.Single(calls));

        var second = await PublishAsync(Question(subject: Task123));
        pane.WaitForAssertion(() => Assert.Equal(2, calls.Count));
        Assert.True(calls[0].Token.IsCancellationRequested, "The older resolution was not cancelled.");

        calls[1].Answer.SetResult(new Dictionary<Guid, string?> { [first] = "backlog", [second] = "backlog" });
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(second))));

        calls[0].Answer.SetResult(new Dictionary<Guid, string?> { [first] = "fincent" });
        await pane.InvokeAsync(() => { });
        pane.Render();

        Assert.NotNull(pane.Find(Row(first)));
    }

    [Fact]
    public async Task Until_the_first_load_is_back_the_subtitle_makes_no_claim_about_what_is_waiting()
    {
        var id = await PublishAsync(Question());
        var (calls, held) = HeldAnswers();
        var pane = _context.Render<NotificationCentrePane>(p => p
            .Add(c => c.RepositoryScope, ["backlog"])
            .Add(c => c.RepositoriesOf, held));
        pane.WaitForAssertion(() => Assert.Single(calls));

        Assert.Equal("Loading…", pane.Find("[data-testid='notification-centre-subtitle']").TextContent);

        calls[0].Answer.SetResult(new Dictionary<Guid, string?> { [id] = "backlog" });
        pane.WaitForAssertion(() => Assert.Equal(
            "1 waiting for an answer",
            pane.Find("[data-testid='notification-centre-subtitle']").TextContent));
    }

    [Fact]
    public async Task Closing_the_centre_cancels_a_resolution_still_running()
    {
        await PublishAsync(Question());
        var (calls, held) = HeldAnswers();
        var pane = _context.Render<NotificationCentrePane>(p => p
            .Add(c => c.RepositoryScope, ["backlog"])
            .Add(c => c.RepositoriesOf, held));
        pane.WaitForAssertion(() => Assert.Single(calls));

        pane.Instance.Dispose();

        Assert.True(calls[0].Token.IsCancellationRequested);
    }

    [Fact]
    public async Task A_focus_on_a_notification_the_scope_and_a_filter_hide_clears_both_so_the_jump_lands()
    {
        var (backlog, fincent, _) = await PublishThreeRepositoriesAsync();
        var pane = RenderScoped("backlog");
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(backlog))));
        pane.Find("[data-testid='notification-centre-kind-alerts']").Click();

        pane.Render(p => p.Add(c => c.FocusNotificationId, fincent));

        pane.WaitForAssertion(() => Assert.Equal("true", pane.Find(Row(fincent)).GetAttribute("aria-current")));
        Assert.Equal("true", pane.Find("[data-testid='notification-centre-kind-all']").GetAttribute("aria-checked"));
    }

    // --- Group by -------------------------------------------------------------------------

    private string[] Headings(IRenderedComponent<NotificationCentrePane> pane) =>
        [.. pane.FindAll("[data-testid='notification-group'] h3").Select(heading => heading.TextContent)];

    [Fact]
    public async Task With_nothing_scoped_the_list_groups_by_repository_and_the_unfiled_sit_under_brain()
    {
        await PublishThreeRepositoriesAsync();

        var pane = RenderScoped();

        pane.WaitForAssertion(() => Assert.Equal(["Brain", "fincent", "backlog"], Headings(pane)));
        Assert.Equal("repository", pane.Find("[data-testid='notification-centre-grouping'] select").GetAttribute("value"));
    }

    [Fact]
    public async Task With_one_repository_scoped_the_list_groups_by_system()
    {
        await PublishThreeRepositoriesAsync();

        var pane = RenderScoped("backlog");

        pane.WaitForAssertion(() => Assert.Equal(["Claude Code"], Headings(pane)));
    }

    [Fact]
    public async Task Group_by_asked_by_puts_every_agents_question_in_one_group()
    {
        static NotificationSubject Question() => new("spec-manager", "question", Guid.NewGuid().ToString("D"));
        for (var i = 0; i < 3; i++) await PublishAsync(Drafts.Question(subject: Question()) with { Origin = ClaudeCode });

        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.Single(Headings(pane)));
        pane.Find("[data-testid='notification-centre-grouping'] select").Change("subject");
        pane.WaitForAssertion(() => Assert.Equal(3, Headings(pane).Length));
        Assert.DoesNotContain(Headings(pane), heading => heading.Contains('-', StringComparison.Ordinal) && Guid.TryParse(heading.Split(' ')[^1], out _));

        pane.Find("[data-testid='notification-centre-grouping'] select").Change("asked-by");

        pane.WaitForAssertion(() => Assert.Equal(["AI agent"], Headings(pane)));
    }

    [Fact]
    public async Task Without_a_host_answer_group_by_offers_no_repository()
    {
        await PublishAsync(Question());

        var pane = _context.Render<NotificationCentrePane>();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-centre-grouping']")));
        var options = pane.FindAll("[data-testid='notification-centre-grouping'] option").Select(option => option.GetAttribute("value")).ToList();
        Assert.Equal(["system", "asked-by", "subject", "none"], options);
    }

    // --- Filters --------------------------------------------------------------------------

    private static readonly NotificationOrigin TypedInSpecManager =
        new("spec-manager", "fincent · #7 Rounding", "Job", "typed in spec-manager");

    [Fact]
    public async Task Asked_by_narrows_to_a_persons_questions_and_each_option_counts_what_it_keeps()
    {
        var agent = await PublishAsync(Question() with { Origin = ClaudeCode });
        var person = await PublishAsync(Question(subject: Task123) with { Origin = TypedInSpecManager });
        await PublishAsync(Alert());

        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(agent))));
        Assert.Equal(
            ["3", "1", "1"],
            pane.FindAll("[data-testid='notification-centre-asked-by'] [data-testid='notification-centre-tally']").Select(tally => tally.TextContent));

        pane.Find("[data-testid='notification-centre-asked-by-person']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(pane.Find(Row(person)));
            Assert.Empty(pane.FindAll(Row(agent)));
        });
        Assert.Equal("true", pane.Find(Row(person)).GetAttribute("aria-current"));
        Assert.Equal("1 waiting for an answer · 1 hidden by the filters", pane.Find("[data-testid='notification-centre-subtitle']").TextContent);
    }

    [Fact]
    public async Task Kind_narrows_to_the_alerts_and_clear_filters_lets_everything_back_but_keeps_group_by()
    {
        var question = await PublishAsync(Question());
        var alert = await PublishAsync(Alert());

        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));
        Assert.Empty(pane.FindAll("[data-testid='notification-centre-clear-filters']"));
        pane.Find("[data-testid='notification-centre-grouping'] select").Change("subject");

        pane.Find("[data-testid='notification-centre-kind-alerts']").Click();
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll(Row(question))));
        Assert.NotNull(pane.Find(Row(alert)));

        pane.Find("[data-testid='notification-centre-clear-filters']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));
        Assert.Equal("subject", pane.Find("[data-testid='notification-centre-grouping'] select").GetAttribute("value"));
        Assert.Empty(pane.FindAll("[data-testid='notification-centre-clear-filters']"));
    }

    // --- Who asked ------------------------------------------------------------------------

    [Fact]
    public async Task Every_row_marks_whether_an_ai_agent_a_person_or_backlog_raised_it()
    {
        var agent = await PublishAsync(Question() with { Origin = ClaudeCode });
        var person = await PublishAsync(Question(subject: Task123) with { Origin = TypedInSpecManager });
        var backlog = await PublishAsync(Alert() with { Origin = new NotificationOrigin("Backlog", "Pairing") });

        var list = _context.Render<NotificationList>(p => p
            .Add(c => c.Notifications, _centre.ListAsync(Ct).Result)
            .Add(c => c.Now, _time.GetUtcNow())
            .Add(c => c.ThisDeviceId, Owner));

        string Asker(Guid id) => list.Find($"{Row(id)} [data-testid='notification-row-asker']").GetAttribute("data-asker")!;
        Assert.Equal("agent", Asker(agent));
        Assert.Equal("person", Asker(person));
        Assert.Equal("system", Asker(backlog));
    }

    // --- Remembered on this device --------------------------------------------------------

    private sealed class RememberedChoices(NotificationCentreChoices choices) : INotificationCentrePreferences
    {
        public NotificationCentreChoices Choices { get; private set; } = choices;

        public void Remember(NotificationCentreChoices choices) => Choices = choices;
    }

    [Fact]
    public async Task The_centre_reopens_on_the_group_by_and_filters_chosen_last_on_this_device()
    {
        var question = await PublishAsync(Question());
        var alert = await PublishAsync(Alert());
        var remembered = new RememberedChoices(new NotificationCentreChoices("subject", Kind: "alerts"));
        _context.Services.AddSingleton<INotificationCentrePreferences>(remembered);

        var pane = RenderScoped();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(alert))));
        Assert.Empty(pane.FindAll(Row(question)));
        Assert.Equal("subject", pane.Find("[data-testid='notification-centre-grouping'] select").GetAttribute("value"));

        pane.Find("[data-testid='notification-centre-asked-by-ai-agent']").Click();
        Assert.Equal(new NotificationCentreChoices("subject", "ai-agent", "alerts"), remembered.Choices);

        pane.Find("[data-testid='notification-centre-clear-filters']").Click();
        Assert.Equal(new NotificationCentreChoices("subject"), remembered.Choices);
    }

    [Fact]
    public async Task A_remembered_choice_this_build_does_not_know_falls_back_to_all()
    {
        var question = await PublishAsync(Question());
        _context.Services.AddSingleton<INotificationCentrePreferences>(
            new RememberedChoices(new NotificationCentreChoices("colour", "robot", "maybe", "Nowhere")));

        var pane = RenderScoped();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));
        Assert.Equal("repository", pane.Find("[data-testid='notification-centre-grouping'] select").GetAttribute("value"));
        Assert.Empty(pane.FindAll("[data-testid='notification-centre-clear-filters']"));
    }

    [Fact]
    public async Task A_remembered_kind_open_reads_as_all_with_settled_ones_hidden()
    {
        var question = await PublishAsync(Question());
        var alert = await PublishAsync(Alert());
        var answered = await PublishAsync(Question(subject: Task123));
        await _centre.AnswerAsync(answered, ["yes"], null, cancellationToken: Ct);
        _context.Services.AddSingleton<INotificationCentrePreferences>(
            new RememberedChoices(new NotificationCentreChoices(Kind: "open")));

        var pane = RenderScoped();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(alert))));
        Assert.NotNull(pane.Find(Row(question)));
        Assert.Empty(pane.FindAll(Row(answered)));
        Assert.Equal("true", pane.Find("[data-testid='notification-centre-kind-all']").GetAttribute("aria-checked"));
        Assert.Equal("false", pane.Find("[data-testid='notification-centre-show-settled']").GetAttribute("aria-pressed"));
        Assert.Empty(pane.FindAll("[data-testid='notification-centre-clear-filters']"));
    }

    // --- Show settled ----------------------------------------------------------------------

    /// <summary>Two open — a question and an alert — and three settled: one answered, one
    /// acknowledged, one closed.</summary>
    private async Task<(Guid Question, Guid Alert, Guid Answered, Guid Acknowledged, Guid Closed)> PublishOpenAndSettledAsync()
    {
        var question = await PublishAsync(Question());
        var alert = await PublishAsync(Alert());
        var answered = await PublishAsync(Question(subject: Task123));
        var acknowledged = await PublishAsync(Alert(subject: Task123));
        var closed = await PublishAsync(Question(subject: new NotificationSubject("mcp", "ask", "k")));
        await _centre.AnswerAsync(answered, ["yes"], null, cancellationToken: Ct);
        await _centre.AcknowledgeAsync(acknowledged, Ct);
        await _owner.CloseAsync(closed, "Answered in spec-manager by Ada: Not now", Ct);
        return (question, alert, answered, acknowledged, closed);
    }

    private static string[] Badges(IRenderedComponent<NotificationCentrePane> pane) =>
        [.. pane.FindAll("[data-testid='notification-row-badge']").Select(badge => badge.TextContent)];

    private static string[] Tallies(IRenderedComponent<NotificationCentrePane> pane, string group) =>
        [.. pane.FindAll($"[data-testid='notification-centre-{group}'] [data-testid='notification-centre-tally']").Select(tally => tally.TextContent)];

    [Fact]
    public async Task By_default_the_centre_lists_only_what_is_open()
    {
        var (question, alert, _, _, _) = await PublishOpenAndSettledAsync();

        var pane = RenderScoped();

        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll("[data-testid='notification-group'] li").Count));
        Assert.NotNull(pane.Find(Row(question)));
        Assert.NotNull(pane.Find(Row(alert)));
        Assert.DoesNotContain("Answered", Badges(pane));
        Assert.DoesNotContain("Acknowledged", Badges(pane));
        Assert.DoesNotContain("Closed", Badges(pane));
        Assert.Equal("false", pane.Find("[data-testid='notification-centre-show-settled']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Show_settled_lists_the_settled_ones_after_the_open_and_pressed_again_hides_them()
    {
        var (question, _, answered, acknowledged, closed) = await PublishOpenAndSettledAsync();
        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));
        // It says how many it would add before it is pressed.
        Assert.Equal("3", pane.Find("[data-testid='notification-centre-settled-tally']").TextContent);

        pane.Find("[data-testid='notification-centre-show-settled']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(answered))));
        Assert.NotNull(pane.Find(Row(acknowledged)));
        Assert.NotNull(pane.Find(Row(closed)));
        Assert.Equal("true", pane.Find("[data-testid='notification-centre-show-settled']").GetAttribute("aria-pressed"));
        // An open question still leads the list.
        Assert.Equal("Question", Badges(pane)[0]);

        pane.Find("[data-testid='notification-centre-show-settled']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll(Row(answered))));
        Assert.Empty(pane.FindAll(Row(closed)));
        Assert.NotNull(pane.Find(Row(question)));
    }

    [Fact]
    public async Task The_settled_tally_counts_under_the_filters()
    {
        var (question, _, _, _, _) = await PublishOpenAndSettledAsync();
        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));

        pane.Find("[data-testid='notification-centre-kind-questions']").Click();

        // Of the three settled, two are questions: the answered and the closed one.
        pane.WaitForAssertion(() => Assert.Equal("2", pane.Find("[data-testid='notification-centre-settled-tally']").TextContent));
    }

    [Fact]
    public async Task Every_tally_counts_only_what_would_be_listed_and_kind_all_counts_the_rows()
    {
        var (question, _, _, _, _) = await PublishOpenAndSettledAsync();
        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));

        // All, Questions, Alerts over the two open ones; the settled three are not counted.
        Assert.Equal(["2", "1", "1"], Tallies(pane, "kind"));
        Assert.Equal("2", Tallies(pane, "asked-by")[0]);
        Assert.Equal("From anywhere (2)", pane.FindAll("[data-testid='notification-centre-from'] option")[0].TextContent.Trim());
        Assert.Equal(2, pane.FindAll("[data-testid='notification-group'] li").Count);

        pane.Find("[data-testid='notification-centre-show-settled']").Click();

        pane.WaitForAssertion(() => Assert.Equal(["5", "3", "2"], Tallies(pane, "kind")));
        Assert.Equal(5, pane.FindAll("[data-testid='notification-group'] li").Count);
    }

    [Fact]
    public async Task Hiding_settled_ones_hides_no_open_question_so_the_subtitle_counts_none_as_filtered()
    {
        await PublishOpenAndSettledAsync();

        var pane = RenderScoped();

        pane.WaitForAssertion(() => Assert.Equal(
            "1 waiting for an answer",
            pane.Find("[data-testid='notification-centre-subtitle']").TextContent));
    }

    [Fact]
    public async Task Show_settled_is_remembered_on_this_device_and_the_centre_reopens_with_it()
    {
        var (question, _, answered, _, _) = await PublishOpenAndSettledAsync();
        var remembered = new RememberedChoices(new NotificationCentreChoices());
        _context.Services.AddSingleton<INotificationCentrePreferences>(remembered);
        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));

        pane.Find("[data-testid='notification-centre-show-settled']").Click();

        Assert.Equal(new NotificationCentreChoices(ShowSettled: true), remembered.Choices);

        var reopened = RenderScoped();
        reopened.WaitForAssertion(() => Assert.NotNull(reopened.Find(Row(answered))));
        Assert.Equal("true", reopened.Find("[data-testid='notification-centre-show-settled']").GetAttribute("aria-pressed"));

        reopened.Find("[data-testid='notification-centre-show-settled']").Click();
        Assert.Equal(new NotificationCentreChoices(), remembered.Choices);
    }

    [Fact]
    public async Task Clear_filters_leaves_show_settled_as_it_was()
    {
        var (question, _, answered, _, _) = await PublishOpenAndSettledAsync();
        var remembered = new RememberedChoices(new NotificationCentreChoices(Kind: "questions", ShowSettled: true));
        _context.Services.AddSingleton<INotificationCentrePreferences>(remembered);
        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(answered))));

        pane.Find("[data-testid='notification-centre-clear-filters']").Click();

        Assert.Equal(new NotificationCentreChoices(ShowSettled: true), remembered.Choices);
        Assert.Equal("true", pane.Find("[data-testid='notification-centre-show-settled']").GetAttribute("aria-pressed"));
        Assert.NotNull(pane.Find(Row(question)));
    }

    [Fact]
    public async Task One_answered_here_stays_listed_and_picked_until_another_is_picked()
    {
        var first = await PublishAsync(Question());
        var second = await PublishAsync(Question(subject: Task123));
        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.Equal("true", pane.Find(Row(second)).GetAttribute("aria-current")));

        pane.Find("[data-testid='notification-card-choice-no']").Click();

        pane.WaitForAssertion(() => Assert.Equal("Answered", pane.Find($"{Row(second)} [data-testid='notification-row-badge']").TextContent));
        Assert.Equal("true", pane.Find(Row(second)).GetAttribute("aria-current"));
        Assert.Equal("Not now", pane.Find($"{Row(second)} [data-testid='notification-row-answer']").TextContent);

        pane.Find(Row(first)).Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll(Row(second))));
        Assert.Equal("true", pane.Find(Row(first)).GetAttribute("aria-current"));
    }

    [Fact]
    public async Task A_focus_on_a_settled_notification_lists_the_settled_ones_without_remembering_it()
    {
        var (question, _, _, _, closed) = await PublishOpenAndSettledAsync();
        var remembered = new RememberedChoices(new NotificationCentreChoices());
        _context.Services.AddSingleton<INotificationCentrePreferences>(remembered);
        var pane = RenderScoped();
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find(Row(question))));

        pane.Render(p => p.Add(c => c.FocusNotificationId, closed));

        pane.WaitForAssertion(() => Assert.Equal("true", pane.Find(Row(closed)).GetAttribute("aria-current")));
        Assert.Equal("true", pane.Find("[data-testid='notification-centre-show-settled']").GetAttribute("aria-pressed"));
        Assert.Equal(
            "Answered in spec-manager by Ada: Not now",
            pane.Find("[data-testid='notification-card-answer-text']").TextContent);
        Assert.Equal(new NotificationCentreChoices(), remembered.Choices);
    }

    [Fact]
    public async Task With_everything_settled_and_hidden_the_list_says_nothing_waits_and_the_detail_asks_for_no_pick()
    {
        var question = await PublishAsync(Question());
        await _centre.AnswerAsync(question, ["yes"], null, cancellationToken: Ct);

        var pane = RenderScoped();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='notification-list-none']")));
        Assert.Empty(pane.FindAll("[data-testid='notification-centre-nothing-picked']"));
        // The switch stays, so the settled one is a press away.
        Assert.Equal("1", pane.Find("[data-testid='notification-centre-settled-tally']").TextContent);
    }

    // --- The shell's count ---------------------------------------------------------------

    [Fact]
    public async Task The_waiting_badge_counts_open_questions_only_and_follows_answers()
    {
        var question = await PublishAsync(Question());
        await PublishAsync(Question(subject: Task123));
        await PublishAsync(Alert());

        var badge = _context.Render<NotificationsWaitingBadge>();
        badge.WaitForAssertion(() => Assert.Equal("2", badge.Find("[data-testid='notifications-waiting']").TextContent));

        await _centre.AnswerAsync(question, ["no"], null, cancellationToken: Ct);

        badge.WaitForAssertion(() => Assert.Equal("1", badge.Find("[data-testid='notifications-waiting']").TextContent));
    }

    [Fact]
    public void The_waiting_badge_draws_nothing_while_nothing_waits()
    {
        var badge = _context.Render<NotificationsWaitingBadge>();

        Assert.Empty(badge.FindAll("[data-testid='notifications-waiting']"));
    }
}
