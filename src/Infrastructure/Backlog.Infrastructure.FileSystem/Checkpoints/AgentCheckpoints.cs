using System.Diagnostics;
using System.Globalization;

using Backlog.Infrastructure.Sqlite;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>One checkpoint on disk: when it was taken, how much it holds, and where.</summary>
public sealed record AgentCheckpoint(DateTimeOffset TakenAt, long SizeBytes, string FolderPath);

/// <summary>
/// The safety net taken before an agent's first write: a folder
/// <c>checkpoints/&lt;UTC timestamp&gt;/</c> beside <c>backlog.db</c> holding a consistent copy
/// of the database and of the devbook subject-state and annotation stores, so a run that went
/// wrong can be put back by hand.
/// <para>
/// <b>At most one every <see cref="MinimumInterval"/>.</b> An agent run is many tool calls; a
/// checkpoint per call would copy the database dozens of times a minute for nothing. The newest
/// folder on disk is what the interval is measured from, so a restart does not owe a new one.
/// </para>
/// <para>
/// <b>The database through <see cref="SqliteDatabaseFile.CopyTo"/>, never by reading the
/// file</b>, for the reason local ADR 0010 gives for a backup: in WAL mode the file on disk is
/// behind what the journal holds.
/// </para>
/// <para>
/// <b>Never in the way of the write.</b> A checkpoint that fails is logged, its half-written
/// folder removed, and the call goes on. A failure does not start the interval, so the next
/// write tries again.
/// </para>
/// <para>
/// <b>Only the newest <see cref="Keep"/> are kept.</b> Older ones are deleted after each new
/// one. There is no restore: putting a checkpoint back is a file copy a person makes, as it is
/// for a backup (local ADR 0010). The backup worker commits <c>backlog.db</c> alone and never
/// this folder, and a move of the backlog leaves it behind, because it is not one of
/// <see cref="WorkspaceSettingsStore.OwnedRootFolders"/>.
/// </para>
/// </summary>
public sealed class AgentCheckpoints
{
    /// <summary>The folder beside <c>backlog.db</c> the checkpoints go in.</summary>
    public const string FolderName = "checkpoints";

    /// <summary>How many checkpoints are kept; the rest are deleted, oldest first.</summary>
    public const int Keep = 10;

    /// <summary>The shortest time between two checkpoints.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(30);

    /// <summary>The folder name's format: UTC, sortable, and safe in a Windows path.</summary>
    private const string StampFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>The name a checkpoint is written under until it is whole.</summary>
    private const string PartialPrefix = ".partial-";

    /// <summary>The stores copied beside the database, by their folder under the root.</summary>
    private static readonly string[] CopiedFolders =
    [
        DevbookSubjectStateStore.FolderName,
        DevbookAnnotationStore.FolderName,
    ];

    private readonly Func<string> _rootDirectory;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Action<string, string> _copyDatabase;
    private readonly Lock _gate = new();

    /// <param name="rootDirectory">The storage folder that holds <c>backlog.db</c>, asked on
    /// every call so a moved backlog checkpoints where it now is.</param>
    /// <param name="time">The clock the interval and the folder names are read from.</param>
    /// <param name="log">Where a failed checkpoint is reported.</param>
    /// <param name="copyDatabase">The database copy; <see cref="SqliteDatabaseFile.CopyTo"/>
    /// unless a test puts a failing one in its place.</param>
    public AgentCheckpoints(
        Func<string> rootDirectory,
        TimeProvider? time = null,
        ILogger<AgentCheckpoints>? log = null,
        Action<string, string>? copyDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);

        _rootDirectory = rootDirectory;
        _time = time ?? TimeProvider.System;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _copyDatabase = copyDatabase ?? SqliteDatabaseFile.CopyTo;
    }

    /// <summary>Raised after a checkpoint was taken, so a page listing them can redraw.</summary>
    public event Action? Changed;

    /// <summary>Where the checkpoints are, right now.</summary>
    public string Directory => Path.Combine(_rootDirectory(), FolderName);

    /// <summary>
    /// Takes a checkpoint unless one was taken in the last <see cref="MinimumInterval"/>.
    /// Never throws: a failure is logged and answered as <see langword="false"/>.
    /// </summary>
    /// <returns>Whether a checkpoint was taken.</returns>
    public bool TakeIfDue()
    {
        string? staging = null;

        lock (_gate)
        {
            try
            {
                var now = _time.GetUtcNow();
                // A negative gap - the clock went back, or a folder is stamped ahead of it - is
                // due: a checkpoint from "the future" must not hold the next one off.
                if (List().FirstOrDefault() is { } newest
                    && now - newest.TakenAt is var gap
                    && gap >= TimeSpan.Zero
                    && gap < MinimumInterval)
                {
                    return false;
                }

                var root = _rootDirectory();
                var directory = Path.Combine(root, FolderName);
                var stamp = now.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);
                var folder = Path.Combine(directory, stamp);
                if (System.IO.Directory.Exists(folder)) return false;

                RemoveLeftovers(directory);

                // Written under a name List does not read, and renamed once whole: a copy cut
                // short - a crash, a cleanup that could not delete - never reads as a checkpoint.
                staging = Path.Combine(directory, PartialPrefix + stamp);
                System.IO.Directory.CreateDirectory(staging);

                var database = SqliteTaskRepository.DatabasePathFor(root);
                if (File.Exists(database)) _copyDatabase(database, Path.Combine(staging, Path.GetFileName(database)));

                foreach (var name in CopiedFolders)
                {
                    CopyFolder(Path.Combine(root, name), Path.Combine(staging, name));
                }

                System.IO.Directory.Move(staging, folder);
                staging = null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "The checkpoint before an agent's write could not be taken in {Folder}; the write goes on without one.", staging);
                TryDelete(staging);
                return false;
            }

            Prune();
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A listener to the checkpoints failed; the checkpoint was taken.");
        }

        return true;
    }

    /// <summary>The checkpoints on disk, newest first. A folder whose name is not a
    /// checkpoint's stamp is not one, and is left out and left alone.</summary>
    public IReadOnlyList<AgentCheckpoint> List()
    {
        var directory = Directory;
        if (!System.IO.Directory.Exists(directory)) return [];

        var found = new List<AgentCheckpoint>();
        foreach (var folder in System.IO.Directory.EnumerateDirectories(directory))
        {
            if (!DateTime.TryParseExact(
                    Path.GetFileName(folder),
                    StampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var takenAt))
            {
                continue;
            }

            found.Add(new AgentCheckpoint(new DateTimeOffset(takenAt, TimeSpan.Zero), SizeOf(folder), folder));
        }

        return [.. found.OrderByDescending(checkpoint => checkpoint.TakenAt)];
    }

    /// <summary>Opens one checkpoint's folder in the system's file manager.</summary>
    /// <returns>Null when it opened, else why it did not, in words a person can act on.</returns>
    public static string? ShowInFolder(AgentCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!System.IO.Directory.Exists(checkpoint.FolderPath)) return "That checkpoint is no longer on disk.";

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(checkpoint.FolderPath) { UseShellExecute = true, CreateNoWindow = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return $"The folder could not be opened: {ex.Message}";
        }
    }

    /// <summary>Deletes every checkpoint but the newest <see cref="Keep"/>.</summary>
    private void Prune()
    {
        IReadOnlyList<AgentCheckpoint> all;
        try
        {
            all = List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "The old checkpoints could not be listed for pruning.");
            return;
        }

        foreach (var old in all.Skip(Keep))
        {
            try
            {
                System.IO.Directory.Delete(old.FolderPath, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "An old checkpoint could not be deleted from {Folder}.", old.FolderPath);
            }
        }
    }

    /// <summary>Deletes what an earlier checkpoint left half-written.</summary>
    private static void RemoveLeftovers(string directory)
    {
        if (!System.IO.Directory.Exists(directory)) return;

        foreach (var leftover in System.IO.Directory.EnumerateDirectories(directory, PartialPrefix + "*"))
        {
            TryDelete(leftover);
        }
    }

    private static void CopyFolder(string source, string destination)
    {
        if (!System.IO.Directory.Exists(source)) return;

        System.IO.Directory.CreateDirectory(destination);
        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static long SizeOf(string folder)
    {
        try
        {
            return System.IO.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryDelete(string? folder)
    {
        try
        {
            if (folder is not null && System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the failure being reported is the one that matters.
        }
    }
}
