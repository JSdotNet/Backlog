using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.UI.Components.Menus;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Typing in the open entry's body. The editor shows each keystroke as it lands;
/// the pane around it, and everything else that hears the Tasks state, waits for
/// the save after the pause (<c>.devbook/design/interaction-guidelines.md#save-timing-and-debounce</c>).
/// A redraw of the whole pane and the shell per character is what made the
/// characters arrive late.
/// <para>
/// The debounce runs on a <see cref="FakeTimeProvider"/>, so "before the pause"
/// and "after it" are where the test puts the clock, not how fast the machine is.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksBodyTypingTests
{
    private const string Provision =
        "# Provision the box\n" +
        "`task` `!ready`\n\n" +
        "Rack it first.\n";

    private static readonly TimeSpan JustShortOfTheDebounce = TimeSpan.FromMilliseconds(749);

    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    [Fact]
    public async Task Typing_in_the_body_announces_nothing_until_the_pause_saves_it()
    {
        var clock = new FakeTimeProvider();
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, [], clock: clock);
        var row = await host.WriteEntryAsync(Provision);
        await host.OpenAsync(row);

        var raised = 0;
        host.State.Changed += () => raised++;

        foreach (var body in new[] { "R", "Re", "Rea", "Read it" })
        {
            host.State.ChangeBody(row, body);
        }

        clock.Advance(JustShortOfTheDebounce);

        Assert.Equal(0, raised);
        Assert.Equal("Read it", row.PreviewBody.Trim());
        Assert.DoesNotContain("Read it", await StoredTextAsync(host, row), StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromMilliseconds(1));

        await WaitUntilSavedAsync(host, row, "Read it");
        Assert.True(raised >= 1, "The save after the pause tells the screens.");
    }

    [Fact]
    public async Task A_keystroke_inside_the_pause_starts_it_again()
    {
        var clock = new FakeTimeProvider();
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, [], clock: clock);
        var row = await host.WriteEntryAsync(Provision);
        await host.OpenAsync(row);

        host.State.ChangeBody(row, "First words.");
        clock.Advance(TimeSpan.FromMilliseconds(500));
        host.State.ChangeBody(row, "First words, then more.");
        clock.Advance(TimeSpan.FromMilliseconds(500));

        // A second past the first keystroke, half of one past the second: neither
        // text has been saved on its own.
        Assert.DoesNotContain("First words", await StoredTextAsync(host, row), StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromMilliseconds(250));

        await WaitUntilSavedAsync(host, row, "First words, then more.");
    }

    [Fact]
    public async Task Typing_in_the_body_redraws_the_editor_and_leaves_the_pane_until_the_save()
    {
        var clock = new FakeTimeProvider();
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, [], clock: clock);
        var row = await host.WriteEntryAsync(Provision);
        await host.OpenAsync(row);

        var pane = host.Render();
        var paneBefore = PaneRenders(pane);
        var editorBefore = BodyEditor(pane).RenderCount;

        string[] typed = ["Rack it first.\n\n## C", "Rack it first.\n\n## Cable it"];
        foreach (var text in typed)
        {
            await pane.Find("[data-testid='entry-body-editor'] textarea").InputAsync(new() { Value = text });
        }

        Assert.Equal(paneBefore, PaneRenders(pane));
        Assert.True(BodyEditor(pane).RenderCount >= editorBefore + typed.Length, "The editor draws every keystroke.");
        Assert.Equal(typed[^1], pane.Find("[data-testid='entry-body-editor'] textarea").GetAttribute("value"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(row)}'] .task-item__detail--steps"));

        clock.Advance(TimeSpan.FromSeconds(1));

        pane.WaitForAssertion(
            () =>
            {
                Assert.True(PaneRenders(pane) > paneBefore, "The save after the pause redraws the pane.");
                Assert.Contains("0 of 1", pane.Find($"[data-testid='{RowTestId(row)}'] .task-item__detail--steps").TextContent, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        // The redraw the save brought did not put an older text back in the box.
        Assert.Equal(typed[^1], pane.Find("[data-testid='entry-body-editor'] textarea").GetAttribute("value"));
    }

    [Theory]
    [InlineData("ArrowLeft")]
    [InlineData("a")]
    [InlineData("Enter")]
    public async Task A_key_the_entry_does_not_act_on_leaves_the_pane_undrawn(string key)
    {
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, []);
        var row = await host.WriteEntryAsync(Provision);
        await host.OpenAsync(row);

        var pane = host.Render();
        var paneBefore = PaneRenders(pane);

        // The key walks up from the body to the entry's own key handler, which
        // acts on Escape and Ctrl+Shift+M and nothing else.
        await pane.Find("[data-testid='entry-body-editor'] textarea").KeyDownAsync(new KeyboardEventArgs { Key = key });
        await pane.Find("[data-testid='entry-detail']").KeyDownAsync(new KeyboardEventArgs { Key = key });

        Assert.Equal(paneBefore, PaneRenders(pane));
    }

    [Fact]
    public async Task Escape_in_the_body_still_closes_the_entry()
    {
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, []);
        var row = await host.WriteEntryAsync(Provision);
        await host.OpenAsync(row);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-body-editor'] textarea").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='entry-detail']")), TimeSpan.FromSeconds(5));
        Assert.Null(host.State.SelectedRow);
    }

    /// <summary>Renders of <see cref="TasksPane"/> itself, read off its entry menu
    /// for the reason <see cref="ShellRenderCount"/> gives.</summary>
    private static int PaneRenders(IRenderedComponent<TasksPane> pane) =>
        pane.FindComponents<ContextMenu>().Single(menu => menu.Instance.TestId == "entry-menu").RenderCount;

    private static IRenderedComponent<MarkdownEditor> BodyEditor(IRenderedComponent<TasksPane> pane) =>
        pane.FindComponents<MarkdownEditor>().Single(editor => editor.Instance.TestId == "entry-body-editor");

    private static async Task<string> StoredTextAsync(TasksPaneHost host, EntryRow row) =>
        EntryTextParser.ToRawText((await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Single(entry => entry.Id == row.Id));

    private static async Task WaitUntilSavedAsync(TasksPaneHost host, EntryRow row, string text)
    {
        var stored = string.Empty;
        await Polling.WaitUntilAsync(
            async () =>
            {
                stored = await StoredTextAsync(host, row);
                return stored.Contains(text, StringComparison.Ordinal) && host.State.SaveState == AppSaveState.Saved;
            },
            () => $"The debounced save never landed; the store holds: {stored}");
    }
}
