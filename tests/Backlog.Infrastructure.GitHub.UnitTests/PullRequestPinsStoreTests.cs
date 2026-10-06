namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The pull requests pinned on this device: kept on the list until unpinned, read by
/// number whatever state they reach. This PC only — a pin is how one reader keeps one
/// thing in front of them, not a fact about the repository.
/// </summary>
public sealed class PullRequestPinsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "pull-request-pins-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_root, "pull-request-pins.json");

    private PullRequestPinsStore Store() => new(FilePath);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Nothing_is_pinned_at_first()
    {
        Assert.Empty(Store().Pins);
    }

    [Fact]
    public void A_pin_is_kept_and_survives_a_restart()
    {
        var store = Store();

        store.Pin("JSdotNet/Backlog", 712);

        Assert.True(store.IsPinned("JSdotNet/Backlog", 712));
        Assert.Equal([new PullRequestPin("JSdotNet/Backlog", 712)], Store().Pins);
    }

    [Fact]
    public void A_pin_names_its_repository_without_regard_to_case_and_is_kept_once()
    {
        var store = Store();

        store.Pin("JSdotNet/Backlog", 712);
        store.Pin("jsdotnet/backlog", 712);

        Assert.True(store.IsPinned("JSDOTNET/BACKLOG", 712));
        Assert.Single(store.Pins);
    }

    [Fact]
    public void Unpinning_forgets_the_pin_and_survives_a_restart()
    {
        var store = Store();
        store.Pin("JSdotNet/Backlog", 712);
        store.Pin("JSdotNet/Archify", 3);

        store.Unpin("jsdotnet/backlog", 712);

        Assert.False(store.IsPinned("JSdotNet/Backlog", 712));
        Assert.Equal([new PullRequestPin("JSdotNet/Archify", 3)], Store().Pins);
    }

    [Fact]
    public void A_change_is_announced_and_a_non_change_is_not()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        store.Pin("JSdotNet/Backlog", 712);
        store.Pin("JSdotNet/Backlog", 712);
        store.Unpin("JSdotNet/Backlog", 1);
        store.Unpin("JSdotNet/Backlog", 712);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void An_unreadable_file_reads_as_nothing_pinned()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, "{ not json");

        var store = Store();

        Assert.Empty(store.Pins);
        store.Pin("JSdotNet/Backlog", 712);
        Assert.True(Store().IsPinned("JSdotNet/Backlog", 712));
    }

    /// <summary>A hand-edited row the store would have refused to write is dropped on
    /// read, so it never reaches a read of GitHub as a repository failure.</summary>
    [Fact]
    public void A_row_naming_a_blank_owner_or_name_is_dropped_on_read()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, """{ "pins": [ { "repository": " /Backlog", "number": 1 }, { "repository": "JSdotNet/ ", "number": 2 }, { "repository": "JSdotNet/Backlog", "number": 3 } ] }""");

        Assert.Equal([new PullRequestPin("JSdotNet/Backlog", 3)], Store().Pins);
    }

    /// <summary>The file is replaced whole, through a file beside it, so a write cut
    /// short never leaves half a file that would read as nothing pinned.</summary>
    [Fact]
    public void A_write_leaves_only_the_pins_file_behind()
    {
        var store = Store();

        store.Pin("JSdotNet/Backlog", 1);
        store.Pin("JSdotNet/Backlog", 2);

        Assert.Equal([FilePath], Directory.GetFiles(_root));
        Assert.Equal(2, Store().Pins.Count);
    }

    [Theory]
    [InlineData(" /Backlog", 1)]
    [InlineData("", 1)]
    [InlineData("not-a-repository", 1)]
    [InlineData("JSdotNet/Backlog", 0)]
    public void A_pin_that_names_no_pull_request_is_refused(string repository, int number)
    {
        var store = Store();

        Assert.Throws<ArgumentException>(() => store.Pin(repository, number));
    }
}
