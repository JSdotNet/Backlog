using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox surface opened by a jump, <c>item=&lt;id&gt;</c>: how an event's Talk note opens
/// (<c>.devbook/domain/events/features.md#linking-notes</c>). The query arrives before the
/// items are read, so the jump waits for the opening read and then opens the item in the slice
/// it is filed in; a later jump to the same id, a new query, opens it again.
/// </summary>
public sealed class InboxSurfaceJumpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-surface-tests", Guid.NewGuid().ToString("n"));
    private readonly BunitContext _context = new();
    private readonly FakeInboxItems _inbox;

    public InboxSurfaceJumpTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton(new GitHubSettingsStore(Path.Combine(_root, "github.json")));
        TasksTestHost.AddToastChannel(_context.Services);
        _inbox = InboxTestHost.AddInboxState(_context.Services);
    }

    private InboxDesktopState State => _context.Services.GetRequiredService<InboxDesktopState>();

    private static Dictionary<string, string> Jump(Guid id) =>
        new(StringComparer.Ordinal) { [InboxSurfaceRegistration.ItemKey] = id.ToString("D") };

    [Fact]
    public void A_jump_before_the_inbox_was_read_opens_the_note_in_its_list_once_the_items_are_there()
    {
        var talks = _inbox.SeedList("Talks");
        var note = _inbox.Seed("Keynote: Aspire in production", kind: ContentKind.Note, listId: talks.Id);
        _inbox.Seed("Something else");

        var surface = _context.Render<InboxSurface>(p => p.Add(c => c.Query, Jump(note.Id)));

        surface.WaitForAssertion(() => Assert.Equal(note.Id, State.SelectedItemId));
        Assert.Equal(InboxDesktopState.ListNavId(talks.Id), State.SelectedSliceId);
    }

    [Fact]
    public void A_second_jump_to_the_same_note_after_picking_another_opens_it_again()
    {
        var note = _inbox.Seed("Keynote: Aspire in production", kind: ContentKind.Note);
        var other = _inbox.Seed("Something else");
        var surface = _context.Render<InboxSurface>(p => p.Add(c => c.Query, Jump(note.Id)));
        surface.WaitForAssertion(() => Assert.Equal(note.Id, State.SelectedItemId));

        surface.InvokeAsync(() => State.SelectItem(other.Id));
        Assert.Equal(other.Id, State.SelectedItemId);

        // The same query handed back by a re-render is not a jump.
        surface.Render(p => p.Add(c => c.Query, surface.Instance.Query));
        Assert.Equal(other.Id, State.SelectedItemId);

        surface.Render(p => p.Add(c => c.Query, Jump(note.Id)));

        surface.WaitForAssertion(() => Assert.Equal(note.Id, State.SelectedItemId));
    }

    [Fact]
    public void A_jump_to_an_id_the_inbox_does_not_hold_opens_the_inbox_as_it_stood()
    {
        _inbox.Seed("Something else");

        var surface = _context.Render<InboxSurface>(p => p.Add(c => c.Query, Jump(Guid.NewGuid())));

        surface.WaitForAssertion(() => Assert.True(State.Loaded));
        Assert.Null(State.SelectedItemId);
        Assert.Equal(InboxDesktopState.InboxSliceId, State.SelectedSliceId);
    }

    public void Dispose()
    {
        _context.Dispose();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
