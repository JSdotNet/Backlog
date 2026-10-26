using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Queueing a session on a prompt entry and the waiting chip (local ADR 0038;
/// <c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>): who is
/// offered the queue icon on the row and in the open entry, the repository choice,
/// the disabled reason, Cancel, and the chip through Requested, Claimed and Started
/// until the linked session takes its place.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksSessionRequestTests
{
    private const string Backlog = "JSdotNet/Backlog";
    private const string Tools = "JSdotNet/Tools";
    private const string BacklogClone = @"D:\Repos\Backlog";

    private static string Prompt(params string[] repositories) =>
        "# Add the Start session action\n`prompt`" + string.Concat(repositories.Select(r => $" `repo:{r}`")) + "\n\nBuild it.\n";

    private static async Task<(TasksPaneHost Host, FakeEntrySessionRequests Queue, FakeTimeProvider Clock)> HostAsync(
        bool cloneBacklog = true,
        bool cloneTools = false)
    {
        var host = await TasksPaneHost.CreateAsync(Backlog, Tools);
        if (cloneBacklog) host.GitHub.Settings.SetCloneDirectory("backlog", BacklogClone);
        if (cloneTools) host.GitHub.Settings.SetCloneDirectory("tools", @"D:\Repos\Tools");

        var queue = new FakeEntrySessionRequests();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        host.Context.Services.AddSingleton<IEntrySessionRequests>(queue);
        host.Context.Services.AddSingleton<TimeProvider>(clock);

        return (host, queue, clock);
    }

    // --- Who is offered it ----------------------------------------------------

    [Fact]
    public async Task A_prompt_whose_repository_has_a_clone_folder_queues_a_session_there_from_the_open_entry()
    {
        var (host, queue, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        await host.OpenAsync(row);

        var pane = host.Render();
        var button = EntryIcon(pane);
        Assert.False(button.HasAttribute("disabled"));
        Assert.Equal($"Queue a session for this entry in {Backlog}", button.GetAttribute("aria-label"));
        Assert.Equal($"Queue a session for this entry in {Backlog}", button.GetAttribute("title"));
        Assert.NotNull(button.QuerySelector("svg.list-end-icon"));

        button.Click();

        pane.WaitForAssertion(() =>
        {
            var asked = Assert.Single(queue.Asked);
            Assert.Equal((row.Id!.Value, Backlog, BacklogClone), asked);
            Assert.Contains("Waiting for a session", pane.Find("[data-testid='session-request-chip']").TextContent, StringComparison.Ordinal);
            Assert.Empty(pane.FindAll("[data-testid='entry-queue-session']"));
            Assert.Empty(pane.FindAll("[data-testid='row-queue-session']"));
        });
        Assert.Contains(host.Toasts.Visible, toast => toast.TestId == "session-request-queued");
    }

    [Fact]
    public async Task The_open_entry_s_icon_sits_on_the_heading_just_before_the_copy_and_the_hand_over_play_button_is_gone()
    {
        var (host, _, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        await host.OpenAsync(row);

        var pane = host.Render();
        var actions = pane.Find("[data-testid='entry-panel-actions']");
        Assert.Equal("entry-panel-actions", EntryIcon(pane).ParentElement?.GetAttribute("data-testid"));
        Assert.Contains("task-panel__action-button", EntryIcon(pane).ClassList);
        Assert.NotNull(actions.NextElementSibling?.QuerySelector("[data-testid='entry-panel-copy']"));

        Assert.Empty(pane.FindAll("[data-testid='start-session-button']"));
        Assert.Empty(pane.FindAll(".entry-doc__actions [data-testid='entry-queue-session']"));
    }

    [Fact]
    public async Task A_saved_prompt_s_list_row_carries_the_icon_in_its_actions_and_queues_from_there()
    {
        var (host, queue, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));

        var pane = host.Render();
        var button = RowIcon(pane, row);
        Assert.NotNull(button.Closest(".task-item__actions"));
        Assert.Contains("task-item__action-button", button.ClassList);
        Assert.Equal($"Queue a session for this entry in {Backlog}", button.GetAttribute("aria-label"));

        button.Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal((row.Id!.Value, Backlog, BacklogClone), Assert.Single(queue.Asked));
            Assert.Empty(RowOf(pane, row).QuerySelectorAll("[data-testid='row-queue-session']"));
            Assert.NotNull(RowOf(pane, row).QuerySelector("[data-testid='row-session-request']"));
        });
    }

    [Fact]
    public async Task While_the_press_is_on_its_way_both_icons_stay_focusable_and_say_they_are_busy()
    {
        var (host, queue, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        await host.OpenAsync(row);
        queue.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pane = host.Render();
        EntryIcon(pane).Click();

        pane.WaitForAssertion(() =>
        {
            foreach (var icon in new[] { EntryIcon(pane), RowIcon(pane, row) })
            {
                Assert.Equal("true", icon.GetAttribute("aria-busy"));
                Assert.Equal("true", icon.GetAttribute("aria-disabled"));
                Assert.False(icon.HasAttribute("disabled"));
            }
        });

        RowIcon(pane, row).Click();
        queue.Gate.SetResult();

        pane.WaitForAssertion(() =>
        {
            Assert.Single(queue.Asked);
            Assert.Empty(pane.FindAll("[data-testid='entry-queue-session']"));
            Assert.Empty(pane.FindAll("[data-testid='row-queue-session']"));
        });
    }

    [Fact]
    public async Task A_press_while_a_request_is_on_its_way_does_not_open_the_repository_chooser()
    {
        var (host, queue, _) = await HostAsync(cloneBacklog: true, cloneTools: true);
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog, Tools));
        await host.OpenAsync(row);
        queue.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pane = host.Render();
        EntryIcon(pane).Click();
        pane.Find($"[data-testid='session-chooser-{Backlog}']").Click();
        pane.WaitForAssertion(() =>
        {
            Assert.Single(queue.Asked);
            Assert.Empty(pane.FindAll("[data-testid='session-chooser']"));
            Assert.Equal("true", RowIcon(pane, row).GetAttribute("aria-disabled"));
        });

        RowIcon(pane, row).Click();
        EntryIcon(pane).Click();

        Assert.Empty(pane.FindAll("[data-testid='session-chooser']"));

        queue.Gate.SetResult();

        pane.WaitForAssertion(() =>
        {
            Assert.Single(queue.Asked);
            Assert.Empty(pane.FindAll("[data-testid='session-chooser']"));
        });
    }

    [Theory]
    [InlineData("task")]
    [InlineData("test")]
    [InlineData("idea")]
    public async Task A_task_test_or_idea_entry_is_not_offered_it(string type)
    {
        var (host, _, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync($"# Not a prompt\n`{type}` `repo:{Backlog}`\n");
        await host.OpenAsync(row);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-queue-session']"));
        Assert.Empty(pane.FindAll("[data-testid='row-queue-session']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-panel-actions']"));
    }

    [Fact]
    public async Task Nothing_is_offered_while_the_switch_is_off()
    {
        var (host, queue, _) = await HostAsync();
        using var _host = host;
        queue.IsOffered = false;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        var other = await host.WriteEntryAsync(AnotherPrompt(Backlog));
        queue.Open[row.Id!.Value] = new(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog);
        await host.OpenAsync(other);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-queue-session']"));
        Assert.Empty(pane.FindAll("[data-testid='row-queue-session']"));
        Assert.Empty(pane.FindAll("[data-testid='session-request']"));
        Assert.Empty(pane.FindAll("[data-testid='row-session-request']"));

        // The entry holding the open request, opened: neither its waiting chip nor
        // its queue icon shows with the switch off.
        await pane.InvokeAsync(() => host.OpenAsync(row));

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("Add the Start session action", pane.Find("[data-testid='entry-detail']").TextContent, StringComparison.Ordinal);
            Assert.Empty(pane.FindAll("[data-testid='session-request']"));
            Assert.Empty(pane.FindAll("[data-testid='entry-queue-session']"));
        });
    }

    [Fact]
    public async Task A_host_with_no_queue_offers_nothing()
    {
        using var host = await TasksPaneHost.CreateAsync(Backlog);
        host.GitHub.Settings.SetCloneDirectory("backlog", BacklogClone);
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        await host.OpenAsync(row);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-queue-session']"));
        Assert.Empty(pane.FindAll("[data-testid='row-queue-session']"));
    }

    [Fact]
    public async Task A_repository_with_no_clone_folder_disables_it_and_names_the_folder_missing()
    {
        var (host, queue, _) = await HostAsync(cloneBacklog: false);
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        await host.OpenAsync(row);

        var pane = host.Render();
        var expected = $"No clone folder is set for {Backlog}. Set one in Settings › Repositories to start a session there.";

        foreach (var (button, reasonTestId) in new[] { (EntryIcon(pane), "entry-queue-session-reason"), (RowIcon(pane, row), "row-queue-session-reason") })
        {
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal("Queue a session for this entry", button.GetAttribute("aria-label"));
            Assert.Equal(expected, button.GetAttribute("title"));
            var reason = pane.Find($"[id='{button.GetAttribute("aria-describedby")}']");
            Assert.Equal(reasonTestId, reason.GetAttribute("data-testid"));
            Assert.Contains("sr-only", reason.ClassList);
            Assert.Equal(expected, reason.TextContent);
        }

        EntryIcon(pane).Click();
        RowIcon(pane, row).Click();
        Assert.Empty(queue.Asked);
    }

    [Fact]
    public async Task Each_disabled_icon_names_its_own_reason_so_no_id_repeats()
    {
        var (host, _, _) = await HostAsync(cloneBacklog: false);
        using var _host = host;
        var first = await host.WriteEntryAsync(Prompt(Backlog));
        var second = await host.WriteEntryAsync(AnotherPrompt(Backlog));
        await host.OpenAsync(first);

        var pane = host.Render();
        var describedBy = new[] { EntryIcon(pane), RowIcon(pane, first), RowIcon(pane, second) }
            .Select(button => button.GetAttribute("aria-describedby"))
            .ToList();

        Assert.All(describedBy, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(describedBy.Count, describedBy.Distinct(StringComparer.Ordinal).Count());
        Assert.All(describedBy, id => Assert.Single(pane.FindAll($"[id='{id}']")));
    }

    [Fact]
    public async Task A_prompt_naming_no_repository_disables_it_and_says_why()
    {
        var (host, _, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt());
        await host.OpenAsync(row);

        var pane = host.Render();
        var button = EntryIcon(pane);

        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal(SessionStartOffer.NoRepository, button.GetAttribute("title"));
        Assert.Equal(SessionStartOffer.NoRepository, RowIcon(pane, row).GetAttribute("title"));
    }

    // --- The repository choice ------------------------------------------------

    [Fact]
    public async Task A_prompt_naming_several_repositories_asks_which_and_queues_in_the_one_picked()
    {
        var (host, queue, _) = await HostAsync(cloneBacklog: true, cloneTools: true);
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog, Tools));
        await host.OpenAsync(row);

        var pane = host.Render();
        var button = EntryIcon(pane);
        Assert.Equal("Queue a session for this entry — choose the repository", button.GetAttribute("aria-label"));

        button.Click();

        var chooser = pane.Find("[data-testid='session-chooser']");
        Assert.Contains("Queue a session in which repository?", chooser.TextContent, StringComparison.Ordinal);
        Assert.Empty(queue.Asked);

        pane.Find($"[data-testid='session-chooser-{Tools}']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal((row.Id!.Value, Tools, @"D:\Repos\Tools"), Assert.Single(queue.Asked));
            Assert.Empty(pane.FindAll("[data-testid='session-chooser']"));
        });
    }

    [Fact]
    public async Task The_row_icon_opens_the_same_choice()
    {
        var (host, queue, _) = await HostAsync(cloneBacklog: true, cloneTools: true);
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog, Tools));

        var pane = host.Render();
        RowIcon(pane, row).Click();

        Assert.NotNull(pane.Find($"[data-testid='session-chooser-{Backlog}']"));
        Assert.Empty(queue.Asked);
    }

    [Fact]
    public async Task The_choice_offers_a_repository_with_no_clone_folder_disabled_with_its_reason()
    {
        var (host, _, _) = await HostAsync(cloneBacklog: true, cloneTools: false);
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog, Tools));
        await host.OpenAsync(row);

        var pane = host.Render();
        EntryIcon(pane).Click();

        Assert.False(pane.Find($"[data-testid='session-chooser-{Backlog}']").HasAttribute("disabled"));
        var tools = pane.Find($"[data-testid='session-chooser-{Tools}']");
        Assert.True(tools.HasAttribute("disabled"));
        Assert.Contains($"No clone folder is set for {Tools}", pane.Find("[data-testid='session-chooser']").TextContent, StringComparison.Ordinal);

        pane.Find("[data-testid='session-chooser-cancel']").Click();
        Assert.Empty(pane.FindAll("[data-testid='session-chooser']"));
    }

    // --- The chip and Cancel ------------------------------------------------

    [Theory]
    [InlineData(EntrySessionRequestState.Requested, true)]
    [InlineData(EntrySessionRequestState.Claimed, false)]
    [InlineData(EntrySessionRequestState.Started, false)]
    public async Task The_chip_shows_while_the_request_is_open_and_Cancel_only_while_Requested(EntrySessionRequestState state, bool cancellable)
    {
        var (host, queue, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        queue.Open[row.Id!.Value] = new(Guid.NewGuid(), state, Backlog);
        await host.OpenAsync(row);

        var pane = host.Render();

        pane.WaitForAssertion(() =>
        {
            var chip = pane.Find("[data-testid='session-request-chip']");
            Assert.Contains("Waiting for a session", chip.TextContent, StringComparison.Ordinal);
            Assert.Contains(Backlog, chip.GetAttribute("title"), StringComparison.Ordinal);
            Assert.Contains("Waiting for a session", pane.Find("[data-testid='row-session-request']").TextContent, StringComparison.Ordinal);
            Assert.Equal(cancellable, pane.FindAll("[data-testid='session-request-cancel']").Count == 1);
            Assert.Empty(pane.FindAll("[data-testid='entry-queue-session']"));
            Assert.Empty(pane.FindAll("[data-testid='row-queue-session']"));
        });
    }

    [Fact]
    public async Task Cancel_withdraws_the_request_and_the_queue_icon_comes_back()
    {
        var (host, queue, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        var requestId = Guid.NewGuid();
        queue.Open[row.Id!.Value] = new(requestId, EntrySessionRequestState.Requested, Backlog);
        await host.OpenAsync(row);

        var pane = host.Render();
        pane.WaitForAssertion(() => pane.Find("[data-testid='session-request-cancel']"));

        pane.Find("[data-testid='session-request-cancel']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([requestId], queue.Cancelled);
            Assert.Empty(pane.FindAll("[data-testid='session-request']"));
            Assert.Empty(pane.FindAll("[data-testid='row-session-request']"));
            Assert.False(EntryIcon(pane).HasAttribute("disabled"));
            Assert.False(RowIcon(pane, row).HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task The_chip_follows_the_queue_and_gives_way_to_the_linked_session()
    {
        var (host, queue, clock) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));
        var requestId = Guid.NewGuid();
        queue.Open[row.Id!.Value] = new(requestId, EntrySessionRequestState.Requested, Backlog);
        await host.OpenAsync(row);

        var pane = host.Render();
        pane.WaitForAssertion(() => pane.Find("[data-testid='session-request-cancel']"));

        // A dispatcher claims it: the chip stays, Cancel goes.
        queue.Open[row.Id!.Value] = new(requestId, EntrySessionRequestState.Claimed, Backlog);
        clock.Advance(TasksSessionRequests.PollInterval);
        pane.WaitForAssertion(() =>
        {
            Assert.Contains("a dispatcher is starting it", pane.Find("[data-testid='session-request-chip']").GetAttribute("title"), StringComparison.Ordinal);
            Assert.Empty(pane.FindAll("[data-testid='session-request-cancel']"));
        });

        // The session links itself: the request is no longer open, and the entry shows
        // the session the way a pasted run's does.
        queue.Open.Clear();
        row.SessionLinks = [new EntrySessionLink(Backlog, "e711d47d-3e09-4254")];
        clock.Advance(TasksSessionRequests.PollInterval);
        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='session-request']"));
            Assert.Empty(pane.FindAll("[data-testid='row-session-request']"));
            Assert.NotEmpty(pane.FindAll("[data-testid='entry-session']"));
        });
    }

    // --- The row menu ---------------------------------------------------------

    [Fact]
    public async Task The_row_menu_has_no_way_to_queue_one_and_offers_Cancel_while_Requested()
    {
        var (host, queue, _) = await HostAsync();
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));

        var pane = host.Render();
        await OpenMenuAsync(pane, row);
        Assert.Empty(pane.FindAll("[data-testid='entry-menu-item-start-session']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-menu-item-cancel-session-request']"));

        // Queue one from the row, then the menu offers to withdraw it.
        await pane.InvokeAsync(() => RowIcon(pane, row).Click());
        pane.WaitForAssertion(() => Assert.Single(queue.Asked));

        await OpenMenuAsync(pane, row);
        Assert.Equal("Cancel session request", MenuLabel(pane, "cancel-session-request"));
        Assert.Empty(pane.FindAll("[data-testid='entry-menu-item-start-session']"));
    }

    [Fact]
    public async Task The_row_menu_offers_nothing_for_a_session_when_there_is_no_clone_folder()
    {
        var (host, _, _) = await HostAsync(cloneBacklog: false);
        using var _host = host;
        var row = await host.WriteEntryAsync(Prompt(Backlog));

        var pane = host.Render();
        await OpenMenuAsync(pane, row);

        Assert.Empty(pane.FindAll("[data-testid='entry-menu-item-start-session']"));
    }

    private static string AnotherPrompt(params string[] repositories) =>
        Prompt(repositories).Replace("Add the Start session action", "Queue a second prompt", StringComparison.Ordinal);

    private static AngleSharp.Dom.IElement EntryIcon(IRenderedComponent<TasksPane> pane) =>
        pane.Find("[data-testid='entry-queue-session']");

    private static AngleSharp.Dom.IElement RowOf(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='entry-list-{row.Id ?? row.Key}']");

    private static AngleSharp.Dom.IElement RowIcon(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        RowOf(pane, row).QuerySelector("[data-testid='row-queue-session']")
            ?? throw new Xunit.Sdk.XunitException($"No queue icon on the row of {row.Id}.");

    private static Task OpenMenuAsync(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='entry-list-{row.Id ?? row.Key}']")
            .ContextMenuAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ClientX = 40, ClientY = 60 });

    private static string MenuLabel(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}'] .menu-list__label").TextContent;

    // --- The offer on its own -------------------------------------------------

    [Fact]
    public void The_offer_keeps_the_entry_s_order_and_drops_repositories_it_names_twice()
    {
        var offer = SessionStartOffer.For(
            EntryType.Prompt,
            persisted: true,
            [Repo("tools", "JSdotNet", "Tools", null), null, Repo("backlog", "JSdotNet", "Backlog", BacklogClone), Repo("backlog", "JSdotNet", "Backlog", BacklogClone)]);

        Assert.NotNull(offer);
        Assert.True(offer.IsEnabled);
        Assert.True(offer.AsksWhichRepository);
        Assert.Equal([Tools, Backlog], offer.Targets.Select(target => target.Repository));
    }

    [Fact]
    public void An_archived_repository_is_not_offered()
    {
        var offer = SessionStartOffer.For(
            EntryType.Prompt,
            persisted: true,
            [new GitHubRepositoryRef("old", "JSdotNet", "Old") { CloneDirectory = @"D:\Repos\Old", Archived = true }]);

        Assert.Equal(SessionStartOffer.NoRepository, offer!.DisabledReason);
    }

    [Fact]
    public void An_unsaved_prompt_offers_nothing()
    {
        Assert.Null(SessionStartOffer.For(EntryType.Prompt, persisted: false, [Repo("backlog", "JSdotNet", "Backlog", BacklogClone)]));
    }

    [Fact]
    public void Several_repositories_without_a_clone_folder_are_all_named()
    {
        var offer = SessionStartOffer.For(
            EntryType.Prompt,
            persisted: true,
            [Repo("a", "o", "a", null), Repo("b", "o", "b", null), Repo("c", "o", "c", null)]);

        Assert.Equal("No clone folder is set for o/a, o/b or o/c. Set one in Settings › Repositories to start a session there.", offer!.DisabledReason);
    }

    private static GitHubRepositoryRef Repo(string alias, string owner, string name, string? clone) =>
        new(alias, owner, name) { CloneDirectory = clone };

    /// <summary>The queue, in memory: what was asked, what was cancelled, and what is
    /// open, which a test sets to move a request on as a dispatcher would.</summary>
    private sealed class FakeEntrySessionRequests : IEntrySessionRequests
    {
        public bool IsOffered { get; set; } = true;

        public Dictionary<Guid, EntrySessionRequest> Open { get; } = [];

        public List<(Guid EntryId, string Repository, string CloneFolder)> Asked { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        public Task<IReadOnlyDictionary<Guid, EntrySessionRequest>> OpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, EntrySessionRequest>>(new Dictionary<Guid, EntrySessionRequest>(Open));

        /// <summary>Set, a request waits on it after it is recorded in
        /// <see cref="Asked"/>, so a test can look at the pane mid-press.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<EntrySessionRequest> RequestAsync(Guid entryId, string repository, string cloneFolder, CancellationToken cancellationToken = default)
        {
            Asked.Add((entryId, repository, cloneFolder));
            if (Gate is { } gate) await gate.Task;

            var request = Open.TryGetValue(entryId, out var open) ? open : new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, repository);
            Open[entryId] = request;
            return request;
        }

        public Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            var held = Open.FirstOrDefault(pair => pair.Value.Id == requestId);
            if (held.Value is not { State: EntrySessionRequestState.Requested }) return Task.FromResult(false);

            Cancelled.Add(requestId);
            Open.Remove(held.Key);
            return Task.FromResult(true);
        }
    }
}
