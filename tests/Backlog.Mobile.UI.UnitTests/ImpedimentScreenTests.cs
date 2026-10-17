using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Progress;
using Backlog.Modules.Progress.Abstractions;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// An impediment's own page on the phone (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>):
/// its age and the goal it holds back, its state as the list that moves it, its check-again
/// day, its note and the tasks it holds back. The three edits it allows each queue exactly
/// one <c>impediment-edit</c> through the device outbox, and the page shows what was asked,
/// waiting, until a desktop has applied it.
/// </summary>
public sealed class ImpedimentScreenTests
{
    private static FakeTimeProvider Clock() => new(StandupFixture.Now);

    private static string Route(Guid id) => $"standup/impediments/{id}";

    [Fact]
    public void The_page_says_how_long_it_has_been_open_and_what_it_holds_back()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open(Route(StandupFixture.EscalatedId));

        app.WaitForAssertion(() => Assert.Equal("Bank sample files missing", app.Find("[data-testid='impediment-title']").TextContent));
        Assert.Equal("4 working days · blocks Customers get paid faster", Squash(app.Find("[data-testid='impediment-line']").TextContent));
        Assert.Equal($"standup/goals/{StandupFixture.PaidId}", app.Find("[data-testid='impediment-goal']").GetAttribute("href"));

        Assert.Equal("2026-10-20", app.Find("[data-testid='impediment-check-again'] input").GetAttribute("value"));
        Assert.Equal("Support silent for 2 days.", app.Find("[data-testid='impediment-note'] textarea").GetAttribute("value"));

        var held = app.FindAll("[data-testid='impediment-held']");
        Assert.Equal(2, held.Count);
        Assert.Contains("Statement format", held[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("status-dot--blocked", held[0].QuerySelector(".status-dot")!.ClassList);
        Assert.Contains("Blocked:", held[0].QuerySelector(".sr-only")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("status-dot--pending", held[1].QuerySelector(".status-dot")!.ClassList);
    }

    [Fact]
    public void The_state_list_ticks_the_states_passed_lights_the_current_one_and_rings_the_rest()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-steps']")));

        var group = app.Find("[data-testid='impediment-steps']");
        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.Equal("State", group.GetAttribute("aria-label"));

        var radios = app.FindAll("[data-testid='impediment-steps'] [role='radio']");
        Assert.Equal(["false", "false", "true", "false", "false"], radios.Select(radio => radio.GetAttribute("aria-checked")));
        Assert.Equal(
            ["passed", "passed", "current", "open", "open"],
            radios.Select(radio => radio.QuerySelector(".impediment-steps__marker")!.GetAttribute("data-phase")));
        Assert.Equal("Reported · bank support Thu 15", Squash(radios[1].TextContent));
        Assert.Equal("Escalated · account manager Fri 16", Squash(radios[2].TextContent));
        Assert.Equal("Being handled", Squash(radios[3].TextContent));

        // It moves forward only: the states behind it are drawn and cannot be taken.
        Assert.True(radios[0].HasAttribute("disabled"));
        Assert.True(radios[1].HasAttribute("disabled"));
        Assert.False(radios[3].HasAttribute("disabled"));
    }

    [Fact]
    public void Choosing_a_state_queues_one_edit_and_shows_it_waiting()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-steps-being-handled']")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='impediment-steps-being-handled']").Click();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-waiting-state']")));
        var edit = Assert.Single(Edits(host));
        Assert.Equal(ImpedimentEditKind.State, edit.Kind);
        Assert.Equal(ImpedimentState.BeingHandled, edit.State);
        Assert.Equal(ImpedimentState.Escalated, edit.WasState);
        Assert.Null(edit.With);

        // Shown as asked, though the impediment the phone holds still says Escalated.
        Assert.Equal("true", app.Find("[data-testid='impediment-steps-being-handled']").GetAttribute("aria-checked"));
        Assert.Equal("Waiting", app.Find("[data-testid='impediment-waiting-state']").TextContent.Trim());
    }

    [Fact]
    public void Setting_the_check_again_day_queues_one_edit()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-check-again'] input")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='impediment-check-again'] input").Change("2026-10-22");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-waiting-checkagain']")));
        var edit = Assert.Single(Edits(host));
        Assert.Equal(ImpedimentEditKind.CheckAgain, edit.Kind);
        Assert.Equal(new DateOnly(2026, 10, 22), edit.CheckAgain);
        Assert.Equal(new DateOnly(2026, 10, 20), edit.WasCheckAgain);
        Assert.Equal("2026-10-22", app.Find("[data-testid='impediment-check-again'] input").GetAttribute("value"));
    }

    [Fact]
    public void Editing_the_note_queues_one_edit_when_it_is_left_and_none_while_typing()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-note'] textarea")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='impediment-note'] textarea").Input("Asked twice");
        Assert.Empty(Edits(host));

        app.Find("[data-testid='impediment-note'] textarea").Change("Asked twice");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-waiting-note']")));
        var edit = Assert.Single(Edits(host));
        Assert.Equal(ImpedimentEditKind.Note, edit.Kind);
        Assert.Equal("Asked twice", edit.Note);
        Assert.Equal("Support silent for 2 days.", edit.WasNote);

        // Committed again with nothing changed, it is not sent again.
        app.Find("[data-testid='impediment-note'] textarea").Change("Asked twice");
        Assert.Single(Edits(host));
    }

    [Fact]
    public void The_arrow_keys_move_through_the_states_and_queue_nothing()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-steps-escalated']")));
        tasks.State = InboxServiceState.Unreachable;

        // Up from Escalated wraps to Resolved: it must not resolve the impediment on the way.
        app.Find("[data-testid='impediment-steps-escalated']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        app.Find("[data-testid='impediment-steps-escalated']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Empty(Edits(host));
        Assert.Equal("true", app.Find("[data-testid='impediment-steps-escalated']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void A_new_impediment_offers_only_the_moves_the_domain_allows()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open(Route(StandupFixture.NewId));

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-steps']")));
        Assert.Equal(
            [false, false, true, true, false],
            app.FindAll("[data-testid='impediment-steps'] [role='radio']").Select(radio => radio.HasAttribute("disabled")));
    }

    [Fact]
    public void A_note_change_and_then_a_state_change_both_queue_in_order()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-note'] textarea")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='impediment-note'] textarea").Change("Asked twice");
        app.Find("[data-testid='impediment-steps-being-handled']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            [ImpedimentEditKind.Note, ImpedimentEditKind.State],
            Edits(host).OrderBy(edit => edit.AskedAt).Select(edit => edit.Kind)));
        Assert.False(app.Find("[data-testid='impediment-note'] textarea").HasAttribute("disabled"));
    }

    /// <summary>A desktop change made since stands
    /// (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>): the phone gives
    /// up the note it asked for and shows the desktop's.</summary>
    [Fact]
    public async Task A_desktop_change_made_since_stands_over_the_note_the_phone_asked_for()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-note'] textarea")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='impediment-note'] textarea").Change("Asked twice");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-waiting-note']")));

        tasks.State = InboxServiceState.Answering;
        tasks.Append(EscalatedWith(note: "Desktop note", at: StandupFixture.Now.AddMinutes(5)));
        await app.InvokeAsync(() => host.Service<ProgressViewProjection>().PullAsync());

        app.WaitForAssertion(() => Assert.Equal("Desktop note", app.Find("[data-testid='impediment-note'] textarea").GetAttribute("value")));
        Assert.Empty(app.FindAll("[data-testid='impediment-waiting-note']"));
    }

    [Fact]
    public async Task A_state_the_desktop_did_not_apply_gives_way_once_a_pull_still_shows_the_old_one()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-steps-being-handled']")));

        app.Find("[data-testid='impediment-steps-being-handled']").Click();
        app.WaitForAssertion(() => Assert.Empty(host.Outbox.Entries));
        Assert.Equal("true", app.Find("[data-testid='impediment-steps-being-handled']").GetAttribute("aria-checked"));

        // The edit has left the phone; the next pull brings the impediment back unchanged.
        tasks.Append(EscalatedWith(note: "Support silent for 2 days.", at: StandupFixture.Now.AddMinutes(5)));
        await app.InvokeAsync(() => host.Service<ProgressViewProjection>().PullAsync());

        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='impediment-steps-escalated']").GetAttribute("aria-checked")));
    }

    [Fact]
    public async Task A_move_that_left_the_phone_still_shows_while_only_other_documents_change()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-steps-being-handled']")));

        app.Find("[data-testid='impediment-steps-being-handled']").Click();
        app.WaitForAssertion(() => Assert.Empty(host.Outbox.Entries));

        // A desktop rewrites the stand-up context on every GitHub read; that says nothing
        // about the move.
        tasks.Append(StandupFixture.Context() with { UpdatedAt = StandupFixture.Now.AddMinutes(10) });
        await app.InvokeAsync(() => host.Service<ProgressViewProjection>().PullAsync());

        Assert.Equal("true", app.Find("[data-testid='impediment-steps-being-handled']").GetAttribute("aria-checked"));
        Assert.Empty(app.FindAll("[data-testid='impediment-waiting-state']"));
    }

    [Fact]
    public void A_double_tap_on_a_state_queues_one_move()
    {
        var tasks = StandupFixture.Service();
        using var host = ShellHost.Paired(clock: Clock(), tasks: tasks);
        var app = host.Open(Route(StandupFixture.EscalatedId));
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-steps-being-handled']")));
        tasks.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='impediment-steps-being-handled']").Click();
        app.Find("[data-testid='impediment-steps-being-handled']").Click();

        app.WaitForAssertion(() => Assert.Single(Edits(host)));
    }

    /// <summary>The escalated impediment as a desktop writes it back, with
    /// <paramref name="note"/> as its note.</summary>
    private static Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskChange EscalatedWith(string note, DateTimeOffset at) =>
        TestTasks.Task("Bank sample files missing", at, type: "impediment", id: StandupFixture.EscalatedId, status: "draft", contentMd: $$"""
            {"version":1,"id":"{{StandupFixture.EscalatedId}}","title":"Bank sample files missing","reportedTo":"account manager",
             "checkAgain":"2026-10-20","note":"{{note}}",
             "history":[{"state":"New","on":"2026-10-14"},{"state":"Reported","on":"2026-10-15","with":"bank support"},
                        {"state":"Escalated","on":"2026-10-16","with":"account manager"}],
             "holdsBack":["{{StandupFixture.StatementId}}","{{StandupFixture.ReconcileId}}"],"createdAt":"2026-10-14T09:00:00+00:00"}
            """);

    [Fact]
    public void An_impediment_this_phone_does_not_hold_says_so_and_leads_back()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service());

        var app = host.Open(Route(Guid.NewGuid()));

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-missing']")));
        Assert.Equal("standup", app.Find("[data-testid='impediment-missing'] a").GetAttribute("href"));
        Assert.Empty(app.FindAll("[data-testid='impediment-steps']"));
    }

    [Fact]
    public void With_goals_off_the_page_shows_no_impediment()
    {
        using var host = ShellHost.Paired(clock: Clock(), tasks: StandupFixture.Service(withContext: false));

        var app = host.Open(Route(StandupFixture.EscalatedId));

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='impediment-off']")));
        Assert.Empty(app.FindAll("[data-testid='impediment-steps']"));
    }

    /// <summary>The impediment edits waiting in the outbox, as the documents they travel as.</summary>
    private static List<Backlog.Modules.Progress.Abstractions.DataTransferObjects.ImpedimentEditDto> Edits(ShellHost host) =>
    [
        .. host.Outbox.Entries
            .Where(entry => entry.Kind == ImpedimentEditOutboxKind.Token)
            .Select(entry => ProgressDocuments.ReadEdit(TaskOutboxKind.Read(entry).Task.ContentMd)!)
    ];

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
