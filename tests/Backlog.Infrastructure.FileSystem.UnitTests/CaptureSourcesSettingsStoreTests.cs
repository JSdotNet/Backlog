using Backlog.Modules.Capture.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Which capture sources are watched, on disk: what the file says before anybody
/// has said anything, what survives a restart, and what it does with a file
/// somebody has edited by hand.
/// <para>
/// Real temp directories rather than a filesystem abstraction, the way
/// <see cref="WorkingHoursSettingsStoreTests"/> does it and for the same reason:
/// what is under test <em>is</em> the file, down to the slugs the kinds are
/// written as.
/// </para>
/// </summary>
public class CaptureSourcesSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "capture-sources-settings-store-tests-" + Guid.NewGuid().ToString("N"));

    public CaptureSourcesSettingsStoreTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "capture-sources.json");

    private CaptureSourcesSettingsStore Store() => new(SettingsFile);

    [Fact]
    public void An_untouched_store_lists_every_monitorable_source_switched_off()
    {
        var store = Store();

        Assert.Equal(CaptureSourceKinds.Monitorable, store.Current.Sources.Select(source => source.Kind).ToArray());
        Assert.All(store.Current.Sources, source =>
        {
            Assert.False(source.Enabled);
            Assert.Empty(source.Targets);
        });
        Assert.Empty(store.Current.Enabled);
    }

    [Fact]
    public void A_source_that_was_switched_on_still_says_so_after_a_restart()
    {
        Assert.Null(Store().SetEnabled(CaptureSourceKind.YouTube, true));

        var reopened = Store().Current;

        Assert.True(reopened.For(CaptureSourceKind.YouTube).Enabled);
        Assert.False(reopened.For(CaptureSourceKind.Website).Enabled);
        Assert.Equal([CaptureSourceKind.YouTube], reopened.Enabled.Select(source => source.Kind).ToArray());
    }

    [Fact]
    public void Targets_are_trimmed_deduplicated_and_survive_a_reload()
    {
        Assert.Null(Store().SetTargets(
            CaptureSourceKind.Website,
            ["  https://example.com/blog ", "https://example.com/blog", string.Empty, "   ", "https://example.org"]));

        var reopened = Store().Current.For(CaptureSourceKind.Website);

        Assert.Equal(["https://example.com/blog", "https://example.org"], reopened.Targets);
    }

    [Fact]
    public void Kinds_are_written_as_their_slugs()
    {
        _ = Store().SetEnabled(CaptureSourceKind.YouTube, true);

        var written = File.ReadAllText(SettingsFile);

        Assert.Contains("\"youtube\"", written, StringComparison.Ordinal);
        Assert.Contains("\"web" + "site\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\": 1", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_nobody_can_read_falls_back_to_the_defaults()
    {
        File.WriteAllText(SettingsFile, "{ this is not json");

        var store = Store();

        Assert.Equal(CaptureSourceKinds.Monitorable, store.Current.Sources.Select(source => source.Kind).ToArray());
        Assert.Empty(store.Current.Enabled);
    }

    /// <summary>A kind this build does not know is dropped rather than guessed
    /// at, and the three it does know are all present whether or not the file
    /// mentioned them.</summary>
    [Fact]
    public void An_unknown_kind_is_dropped_and_the_monitorable_set_is_made_whole()
    {
        File.WriteAllText(
            SettingsFile,
            """
            { "sources": [
                { "kind": "rss", "enabled": true, "targets": ["https://example.com/feed"] },
                { "kind": "email", "enabled": true, "targets": ["news@example.com"] }
            ] }
            """);

        var store = Store();

        Assert.Equal(CaptureSourceKinds.Monitorable, store.Current.Sources.Select(source => source.Kind).ToArray());
        Assert.True(store.Current.For(CaptureSourceKind.Email).Enabled);
        Assert.Equal(["news@example.com"], store.Current.For(CaptureSourceKind.Email).Targets);
        Assert.False(store.Current.For(CaptureSourceKind.YouTube).Enabled);
    }

    /// <summary>Nothing changed is nothing to write and nothing to announce.</summary>
    [Fact]
    public void Setting_what_is_already_set_writes_nothing()
    {
        var store = Store();
        var announced = 0;
        store.Changed += () => announced++;

        Assert.Null(store.SetEnabled(CaptureSourceKind.YouTube, false));
        Assert.Null(store.SetTargets(CaptureSourceKind.YouTube, []));

        Assert.Equal(0, announced);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void Saving_tells_whoever_is_showing_the_sources()
    {
        var store = Store();
        var announced = 0;
        store.Changed += () => announced++;

        _ = store.SetEnabled(CaptureSourceKind.Email, true);
        _ = store.SetTargets(CaptureSourceKind.Email, ["news@example.com"]);

        Assert.Equal(2, announced);
    }

    [Fact]
    public void The_store_says_where_the_sources_are_kept()
    {
        Assert.Equal(SettingsFile, Store().SettingsPath);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
