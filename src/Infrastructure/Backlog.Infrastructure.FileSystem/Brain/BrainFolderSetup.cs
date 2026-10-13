namespace Backlog.Infrastructure.FileSystem;

/// <summary>What the Brain card says about a folder: whether it is there, which
/// sync product carries it, and whether its two convention files are.</summary>
/// <param name="Folder">The folder looked at.</param>
/// <param name="Exists">Whether the folder is on disk.</param>
/// <param name="SyncedBy">The sync provider's folder it sits in, or null when it
/// sits in none this app recognises.</param>
/// <param name="HasReadme">Whether <c>README.md</c> is in it.</param>
/// <param name="HasConventions">Whether <c>CONVENTIONS.md</c> is in it.</param>
public sealed record BrainFolderStatus(
    string Folder,
    bool Exists,
    SyncedFolderMatch? SyncedBy,
    bool HasReadme,
    bool HasConventions)
{
    /// <summary>Whether <b>Create conventions</b> still has something to write.</summary>
    public bool NeedsConventions => !HasReadme || !HasConventions;
}

/// <summary>What <b>Create conventions</b> did: the files and folders it wrote,
/// the ones it found already there and left alone, and why it stopped, if it
/// did.</summary>
public sealed record BrainConventionsResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Kept,
    string? Error = null)
{
    internal static BrainConventionsResult Failed(string error) => new([], [], error);
}

/// <summary>
/// The file-system half of setting up a Brain: reading what a chosen folder holds
/// and scaffolding its convention.
/// <para>
/// <see cref="Inspect"/> reuses <see cref="SyncedFolderDetector"/>, the probe the
/// Storage page already runs on the backlog's folder, and with the opposite
/// reading. There a synced folder is a hazard to the database; here it is the
/// expected home of Markdown notes (local ADR 0026), so the card says so as
/// information. The database the Brain is searched through is built in the app's
/// own storage either way, and nothing here writes one.
/// </para>
/// <para>
/// <see cref="CreateConventions"/> never overwrites. Each file is created with
/// <see cref="FileMode.CreateNew"/>, so a file that appears between looking and
/// writing is still left as it is; and the area folders come from the
/// <c>CONVENTIONS.md</c> the folder ends up with, so a person's own convention
/// decides them rather than the shipped one.
/// </para>
/// </summary>
public sealed class BrainFolderSetup
{
    private readonly Func<string, SyncedFolderMatch?> _detectSyncedFolder;

    public BrainFolderSetup()
        : this(new SyncedFolderDetector().Detect)
    {
    }

    /// <summary>Names the sync probe, because which sync products the machine
    /// running a test has is not something a test may depend on.</summary>
    public BrainFolderSetup(Func<string, SyncedFolderMatch?> detectSyncedFolder)
    {
        _detectSyncedFolder = detectSyncedFolder;
    }

    /// <summary>What <paramref name="folder"/> holds right now.</summary>
    public BrainFolderStatus Inspect(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var exists = Directory.Exists(folder);

        return new BrainFolderStatus(
            folder,
            exists,
            Detect(folder),
            exists && File.Exists(Path.Combine(folder, BrainConventionTemplate.ReadmeFileName)),
            exists && File.Exists(Path.Combine(folder, BrainConventionTemplate.ConventionsFileName)));
    }

    /// <summary>Writes whichever of <c>README.md</c> and <c>CONVENTIONS.md</c> is
    /// missing from the shipped template, then every area folder the folder's
    /// <c>CONVENTIONS.md</c> declares. Everything already there is left alone and
    /// reported as kept.</summary>
    public BrainConventionsResult CreateConventions(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var written = new List<string>();
        var kept = new List<string>();

        try
        {
            Directory.CreateDirectory(folder);

            WriteIfMissing(folder, BrainConventionTemplate.ReadmeFileName, BrainConventionTemplate.Readme, written, kept);
            WriteIfMissing(folder, BrainConventionTemplate.ConventionsFileName, BrainConventionTemplate.Conventions, written, kept);

            var conventions = File.ReadAllText(Path.Combine(folder, BrainConventionTemplate.ConventionsFileName));

            foreach (var area in BrainConventionTemplate.ReadDeclaredAreas(conventions))
            {
                var areaFolder = Path.Combine(folder, area.Folder);
                var name = area.Folder + "/";

                if (Directory.Exists(areaFolder))
                {
                    kept.Add(name);
                    continue;
                }

                Directory.CreateDirectory(areaFolder);
                written.Add(name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new BrainConventionsResult(written, kept, $"Couldn't create the conventions: {ex.Message}");
        }

        return new BrainConventionsResult(written, kept);
    }

    private static void WriteIfMissing(string folder, string fileName, string content, List<string> written, List<string> kept)
    {
        var path = Path.Combine(folder, fileName);

        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
            written.Add(fileName);
        }
        catch (IOException) when (File.Exists(path))
        {
            // CreateNew's refusal: the person's own file, which this never touches.
            kept.Add(fileName);
        }
    }

    private SyncedFolderMatch? Detect(string folder)
    {
        try
        {
            return _detectSyncedFolder(folder);
        }
        catch (Exception)
        {
            // Information, never a fault: a probe that cannot answer says nothing.
            return null;
        }
    }
}
