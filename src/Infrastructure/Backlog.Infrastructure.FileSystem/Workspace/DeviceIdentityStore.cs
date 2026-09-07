using System.Text.Json;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Which machine this installation is, kept in one small file beside the app's other
/// per-user settings.
/// <para>
/// Its own file for the reason <see cref="ShellNavigationStore"/> and
/// <see cref="TasksRefreshSettingsStore"/> have theirs: <c>settings.json</c> holds
/// choices somebody made, and this holds a fact about the box that nobody chose and
/// nobody may edit. Mixing the two would put a value a person can retype next to one
/// that must never change.
/// </para>
/// <para>
/// The folder is the workspace's own app-data folder — <c>Backlog.Debug</c> in a Debug
/// build, <c>Backlog</c> in a release — rather than a fixed <c>Backlog</c>. The identity
/// is the identity of the installation that owns a workspace, and ADR 0005 attaches the
/// pairing credential to it; a Debug build already owns a separate workspace and a
/// separate database, and handing it the release identity would let a developer's
/// pairing attach itself to the real device. The three sibling stores hard-coding
/// <c>Backlog</c> is a pre-existing inconsistency and is deliberately left alone here.
/// </para>
/// <para>
/// Everything happens in the constructor and nothing is watched. A machine is not
/// renamed while the app is open, and the siblings read once too — a watcher would buy
/// a case nobody has at the cost of a contract every consumer would have to subscribe
/// to.
/// </para>
/// <para>
/// <b>What a corrupt file costs.</b> A file whose id is missing or is not a Guid is
/// regenerated in place, which loses the old id. That is acceptable only while nothing
/// is paired to it: today a lost id means sessions read after this point are stamped
/// with a new machine, and nothing else. When ADR 0005's pairing lands, this branch has
/// to become a refusal that asks rather than a rewrite that decides — the sentence is
/// here so that change is made deliberately rather than discovered.
/// </para>
/// <para>
/// <b>What an unreadable file costs.</b> A file that is there but that this process
/// cannot read through — another host holding it open, or an empty one left by a write
/// that never finished — is never overwritten. The store looks again a few times and, if
/// nothing readable arrives, runs on an identity it does not write down. That branch is
/// deliberate and is the same bargain the unwritable folder gets: the run is stamped
/// with an id no later run will use, which costs the sessions read during it, and it is
/// the cheaper of the two mistakes — the other one is stamping over an id that something
/// is already paired to because it could not be read at the wrong moment.
/// </para>
/// </summary>
public sealed class DeviceIdentityStore : IDeviceIdentitySource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>How many times a host that lost the first-write race re-reads before
    /// giving up and running on an identity of its own. Two Debug hosts out of two
    /// worktrees can start in the same second; the winner holds the file exclusively
    /// while it writes, so the loser's first read can arrive before there is anything
    /// to read. A hundred milliseconds is far longer than writing sixty bytes takes and
    /// is only ever spent on that one race.</summary>
    private const int AdoptionAttempts = 5;

    private const int AdoptionPauseMilliseconds = 20;

    private readonly string _path;

    /// <summary>The per-user location: <c>device.json</c> in the same app-data folder
    /// <see cref="WorkspaceSettingsStore"/> puts the workspace pointer in.</summary>
    public DeviceIdentityStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                WorkspaceSettingsStore.DefaultAppDataFolderName,
                "device.json"))
    {
    }

    /// <summary>Names the file separately from the per-user location. Public rather
    /// than internal because it is the only way to give a test — or a harness running
    /// beside another — an identity that does not fight over the real per-user
    /// file.</summary>
    public DeviceIdentityStore(string path)
        : this(path, Environment.MachineName)
    {
    }

    /// <summary>The machine name injected as well, so the rename case can be asserted
    /// without renaming the machine the tests run on.</summary>
    public DeviceIdentityStore(string path, string machineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        _path = path;
        Current = Resolve(machineName);
    }

    /// <inheritdoc />
    public DeviceIdentity Current { get; }

    /// <summary>Where the identity is written. Shown nowhere yet; it exists for the
    /// same reason the sibling stores expose theirs — the pairing screen will have to
    /// be able to say which file it is talking about.</summary>
    public string SettingsPath => _path;

    private DeviceIdentity Resolve(string machineName)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var (outcome, stored) = Read();

            return outcome switch
            {
                ReadOutcome.Found => Adopt(stored, machineName),

                // Read through to the end and found nothing usable. See the class
                // remarks for what regenerating it costs.
                ReadOutcome.Unusable => Mint(machineName, overwrite: true),

                // Not readable rather than not usable — which on a shared app-data
                // folder usually means another host is holding it open while it writes
                // its own first copy. Wait for that copy rather than overwriting it.
                _ when File.Exists(_path) => AdoptTheWinner(machineName, Guid.NewGuid()),

                _ => Mint(machineName, overwrite: false)
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // An identity for this process only. The app opening matters more than the
            // identity being stable, and a host that cannot write its own app-data
            // folder has a larger problem than this file.
            return new DeviceIdentity(Guid.NewGuid(), machineName);
        }
    }

    /// <summary>
    /// The stored identity, with the name brought up to date when the machine has been
    /// renamed since it was written. The id never moves: that is the whole point of
    /// there being one.
    /// <para>
    /// The refresh catches its own write failures rather than letting them travel. A
    /// name that could not be persisted is never a reason to invent an id — outside this
    /// method the failure would reach <see cref="Resolve"/>'s catch and mint a new
    /// identity for a file that had just been read perfectly well, which is precisely the
    /// loss this store exists to prevent. Unwritten, the name is simply refreshed again
    /// on the next start.
    /// </para>
    /// </summary>
    private DeviceIdentity Adopt((Guid Id, string Name) stored, string machineName)
    {
        if (!string.Equals(stored.Name, machineName, StringComparison.Ordinal))
        {
            try
            {
                Write(stored.Id, machineName);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The display name is the only thing lost, and only until the next start.
            }
        }

        return new DeviceIdentity(stored.Id, machineName);
    }

    /// <summary>
    /// A new identity, written down.
    /// <para>
    /// The first write is <see cref="FileMode.CreateNew"/> rather than a plain write, and
    /// that is the cheapest honest guard against two hosts starting together: a Debug
    /// build and the web harness out of two worktrees share one app-data folder, and the
    /// second of them must adopt the first's id rather than overwrite it. Losing the
    /// race is not an error — it is how the loser finds out there is already an answer.
    /// </para>
    /// </summary>
    private DeviceIdentity Mint(string machineName, bool overwrite)
    {
        var minted = Guid.NewGuid();

        if (overwrite)
        {
            Write(minted, machineName);
            return new DeviceIdentity(minted, machineName);
        }

        try
        {
            using var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new StoredIdentity
            {
                Id = minted.ToString(),
                Name = machineName
            }, JsonOptions);

            return new DeviceIdentity(minted, machineName);
        }
        catch (IOException)
        {
            return AdoptTheWinner(machineName, minted);
        }
    }

    /// <summary>
    /// What the loser of the first-write race does: read what the winner wrote, and use
    /// that. The winner holds the file exclusively while it writes, so the first read
    /// can legitimately find nothing — hence the retries rather than a single attempt.
    /// If every attempt comes back empty the process runs on its own minted identity,
    /// which is the same fallback an unwritable folder gets.
    /// </summary>
    private DeviceIdentity AdoptTheWinner(string machineName, Guid minted)
    {
        for (var attempt = 0; attempt < AdoptionAttempts; attempt++)
        {
            var (outcome, theirs) = Read();

            if (outcome is ReadOutcome.Found) return Adopt(theirs, machineName);

            // Nothing after the last look: the answer cannot change between a read this
            // method has already given up on and the line below it.
            if (attempt < AdoptionAttempts - 1) Thread.Sleep(AdoptionPauseMilliseconds);
        }

        return new DeviceIdentity(minted, machineName);
    }

    /// <summary>
    /// What reading the file produced. Three outcomes rather than a nullable identity,
    /// because "there is nothing here yet" and "there is something here and it is
    /// rubbish" call for opposite actions: the first mints and the second overwrites,
    /// and getting them the wrong way round is how one host stamps over another's id.
    /// </summary>
    private enum ReadOutcome
    {
        /// <summary>No file, or a file this process could not open right now.</summary>
        Unreadable,

        /// <summary>Read through to the end, and what was in it is not an identity.</summary>
        Unusable,

        Found
    }

    private (ReadOutcome Outcome, (Guid Id, string Name) Identity) Read()
    {
        try
        {
            if (!File.Exists(_path)) return (ReadOutcome.Unreadable, default);

            var text = File.ReadAllText(_path);

            // A file with nothing in it is not a file that says something wrong. It is a
            // write that has not landed or one that never finished, and the two call for
            // opposite actions: this one is waited for, not overwritten.
            if (string.IsNullOrWhiteSpace(text)) return (ReadOutcome.Unreadable, default);

            var stored = JsonSerializer.Deserialize<StoredIdentity>(text, JsonOptions);

            if (stored is null) return (ReadOutcome.Unusable, default);

            return Guid.TryParse(stored.Id, out var id) && id != Guid.Empty
                ? (ReadOutcome.Found, (id, stored.Name ?? string.Empty))
                : (ReadOutcome.Unusable, default);
        }
        catch (JsonException)
        {
            return (ReadOutcome.Unusable, default);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (ReadOutcome.Unreadable, default);
        }
    }

    /// <summary>
    /// The file replaced whole, never emptied and refilled where a reader can see it.
    /// <para>
    /// <c>File.WriteAllText</c> truncates the real file and then writes into it, which
    /// leaves a window — short, but a window — in which another host reading the same
    /// app-data folder finds a file with nothing in it. Whether that reader classifies
    /// the emptiness as rubbish and mints over the top decides whether one host can
    /// destroy another's identity by being unlucky, and the answer must not depend on
    /// timing. Written to a sibling and moved over the top, a reader sees the whole
    /// previous document or the whole new one and nothing in between. The sibling is in
    /// the same folder so the move is a rename on one volume rather than a copy, and the
    /// <see cref="FileMode.CreateNew"/> first write is already atomic in the same sense:
    /// it holds the file exclusively until it is complete.
    /// </para>
    /// </summary>
    private void Write(Guid id, string machineName)
    {
        var temp = $"{_path}.{Guid.NewGuid():n}.tmp";

        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new StoredIdentity
            {
                Id = id.ToString(),
                Name = machineName
            }, JsonOptions));

            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            // A no-op once the move has landed, and the litter swept up when it has not.
            Discard(temp);
        }
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A temp file that will not delete is litter beside a correct identity, and
            // is not worth failing a start over.
        }
    }

    /// <summary>
    /// The file's shape. The id is held as a string rather than a <see cref="Guid"/>
    /// because a file somebody has hand-edited is a thing that happens, and a string
    /// that fails <see cref="Guid.TryParse"/> is a corrupt file this store can report on
    /// rather than a deserialization exception in a constructor.
    /// </summary>
    private sealed record StoredIdentity
    {
        public string? Id { get; init; }

        public string? Name { get; init; }
    }
}
