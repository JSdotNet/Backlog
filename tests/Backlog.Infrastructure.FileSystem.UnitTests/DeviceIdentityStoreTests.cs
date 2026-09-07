using System.Collections.Concurrent;
using System.Text.Json;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The one file that says which machine this is. What matters about it is that the
/// id is minted once and never again: a session record, and later a pairing
/// credential, are attached to it, so an id that changed on a rename or on a corrupt
/// read would silently orphan everything stamped with the old one.
/// <para>
/// A temporary path per test rather than the real per-user file — the store takes its
/// path for exactly this reason.
/// </para>
/// </summary>
public sealed class DeviceIdentityStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-device-identity-tests",
        Guid.NewGuid().ToString("n"));

    private string IdentityPath => Path.Combine(_root, "device.json");

    [Fact]
    public void A_first_start_mints_an_identity_and_writes_it_down()
    {
        var store = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        Assert.NotEqual(Guid.Empty, store.Current.Id);
        Assert.Equal("DEV-TOWER", store.Current.Name);

        // Written, not only remembered: the next start has to find it.
        Assert.True(File.Exists(IdentityPath));

        var written = Stored();
        Assert.Equal(store.Current.Id.ToString(), written.Id);
        Assert.Equal("DEV-TOWER", written.Name);
    }

    [Fact]
    public void The_second_start_reuses_the_identity_the_first_one_wrote()
    {
        var first = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");
        var second = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        Assert.Equal(first.Current.Id, second.Current.Id);
    }

    /// <summary>
    /// Renaming a PC does not make it a different PC. The name on screen follows the
    /// operating system; the id under it does not move, so the sessions stamped
    /// before the rename still belong to this machine.
    /// </summary>
    [Fact]
    public void An_operating_system_rename_keeps_the_id_and_refreshes_the_name()
    {
        var before = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        var after = new DeviceIdentityStore(IdentityPath, "DEV-STUDIO");

        Assert.Equal(before.Current.Id, after.Current.Id);
        Assert.Equal("DEV-STUDIO", after.Current.Name);

        // Rewritten rather than only reported, or every start would keep finding the
        // old name and keep announcing a rename that happened months ago.
        Assert.Equal("DEV-STUDIO", Stored().Name);
    }

    /// <summary>
    /// The rename is a courtesy; the id is the point. A refresh of the name that cannot
    /// be written down leaves the id exactly where it was — the alternative, which is
    /// what an unguarded write gives, is a new identity minted for a file that had just
    /// been read without trouble.
    /// </summary>
    [Fact]
    public void A_name_that_cannot_be_written_down_does_not_cost_the_identity_its_id()
    {
        var before = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        // Readable, and not replaceable: Windows refuses to move a file over a read-only
        // one, so the read below succeeds and only the rewrite of the name fails.
        File.SetAttributes(IdentityPath, FileAttributes.ReadOnly);

        try
        {
            var after = new DeviceIdentityStore(IdentityPath, "DEV-STUDIO");

            Assert.Equal(before.Current.Id, after.Current.Id);
            Assert.Equal("DEV-STUDIO", after.Current.Name);

            // The write really did fail, or this fact would be asserting nothing.
            Assert.Equal("DEV-TOWER", Stored().Name);
            Assert.Equal(before.Current.Id.ToString(), Stored().Id);
        }
        finally
        {
            File.SetAttributes(IdentityPath, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// A file with nothing in it is a write that never landed, not an identity that says
    /// something wrong, and the store waits for it rather than stamping over it. This is
    /// the truncation window in the small: a host that overwrites an empty file is a host
    /// that would have destroyed the winner's id had it read a hundred microseconds
    /// earlier.
    /// </summary>
    [Fact]
    public void An_empty_file_is_waited_for_rather_than_overwritten()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(IdentityPath, string.Empty);

        var store = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        // An identity for this run, because the app opening still matters more — but the
        // file belongs to whoever wrote it and is left alone.
        Assert.NotEqual(Guid.Empty, store.Current.Id);
        Assert.Equal(string.Empty, File.ReadAllText(IdentityPath));
    }

    /// <summary>
    /// Readers arriving while the name is being rewritten. Every one of them has to come
    /// away with the one id on disk: seeing half a file, calling it rubbish and minting
    /// over it is how a machine loses the identity its sessions are stamped with.
    /// </summary>
    [Fact]
    public void A_reader_arriving_while_the_name_is_rewritten_never_mints_over_it()
    {
        var original = new DeviceIdentityStore(IdentityPath, "DEV-TOWER").Current.Id;
        var ids = new ConcurrentBag<Guid>();

        // Alternating names, so half of these hosts rewrite the file while the other half
        // are reading it.
        Parallel.For(0, 100, index =>
            ids.Add(new DeviceIdentityStore(IdentityPath, index % 2 == 0 ? "DEV-TOWER" : "DEV-STUDIO").Current.Id));

        Assert.Equal([original], ids.Distinct());
        Assert.Equal(original.ToString(), Stored().Id);
    }

    [Fact]
    public void A_file_whose_id_is_not_a_guid_is_regenerated_in_place()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(IdentityPath, """{ "id": "not-a-guid", "name": "DEV-TOWER" }""");

        var store = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        Assert.NotEqual(Guid.Empty, store.Current.Id);
        Assert.Equal(store.Current.Id.ToString(), Stored().Id);
    }

    [Fact]
    public void A_file_that_is_not_json_at_all_is_regenerated_in_place()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(IdentityPath, "half a write, then the power went");

        var store = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        Assert.NotEqual(Guid.Empty, store.Current.Id);
        Assert.Equal(store.Current.Id.ToString(), Stored().Id);
    }

    /// <summary>
    /// Two Debug hosts out of two worktrees share one app-data folder and can start
    /// in the same second. Whichever gets the file first wins, and the others adopt
    /// what it wrote rather than each minting an id of its own — the whole reason the
    /// first write is a <c>CreateNew</c> and not a <c>WriteAllText</c>.
    /// <para>
    /// A barrier and real threads rather than <c>Parallel.For</c>, because the race has
    /// to actually be run. Left to itself the first host finishes writing sixty bytes
    /// long before the second is asked for anything, and every host after the first takes
    /// the ordinary second-start path — a green fact that never touched the branch it
    /// claims to be about. Here nobody looks at the folder until everybody is ready to.
    /// </para>
    /// </summary>
    [Fact]
    public void Hosts_starting_together_settle_on_one_identity()
    {
        const int hosts = 8;

        Directory.CreateDirectory(_root);

        using var together = new Barrier(hosts);
        var stores = new DeviceIdentityStore[hosts];
        var threads = new Thread[hosts];

        for (var index = 0; index < hosts; index++)
        {
            var slot = index;
            threads[slot] = new Thread(() =>
            {
                together.SignalAndWait();
                stores[slot] = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");
            });

            threads[slot].Start();
        }

        foreach (var thread in threads) thread.Join();

        var ids = stores.Select(store => store.Current.Id).Distinct().ToList();

        Assert.Single(ids);
        Assert.Equal(ids[0].ToString(), Stored().Id);
    }

    [Fact]
    public void The_path_it_was_given_is_the_path_it_reports()
    {
        var store = new DeviceIdentityStore(IdentityPath, "DEV-TOWER");

        Assert.Equal(IdentityPath, store.SettingsPath);
    }

    /// <summary>
    /// A folder that cannot be written to must not stop the app from opening. The
    /// process gets an identity for this run and nothing else breaks; what it costs is
    /// that the identity is not stable, which is the lesser of the two failures.
    /// </summary>
    [Fact]
    public void An_unwritable_location_still_yields_an_identity_for_this_run()
    {
        // A file where the folder should be: creating a directory under it fails on
        // every platform, without needing a permission fixture.
        Directory.CreateDirectory(_root);
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a folder");

        var store = new DeviceIdentityStore(Path.Combine(blocked, "device.json"), "DEV-TOWER");

        Assert.NotEqual(Guid.Empty, store.Current.Id);
        Assert.Equal("DEV-TOWER", store.Current.Name);
    }

    private StoredIdentity Stored()
    {
        var dto = JsonSerializer.Deserialize<StoredIdentity>(
            File.ReadAllText(IdentityPath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.NotNull(dto);

        return dto;
    }

    private sealed record StoredIdentity
    {
        public string? Id { get; init; }

        public string? Name { get; init; }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A temp folder that will not delete is not a test failure.
        }
    }
}
