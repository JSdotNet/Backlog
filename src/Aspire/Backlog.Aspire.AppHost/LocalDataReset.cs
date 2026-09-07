using System.Text.Json;

using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.Logging;

namespace Backlog.Aspire.AppHost;

/// <summary>
/// Returns the local-first store to its first-run state.
///
/// <para>Backlog keeps its task database and its workspace settings in a per-user
/// folder rather than in the repository, and a DEBUG build names that folder
/// <c>Backlog.Debug</c> so a developer never lands in a real backlog. Every git
/// worktree of this repository resolves to that same folder, so a reset here is
/// felt by every session running beside this one. That is why it exists only as a
/// resource command someone chooses in the dashboard, and never on a startup
/// path.</para>
///
/// <para>It removes named files rather than the folder. The workspace also holds
/// the <c>_inbox</c> folder and whatever else a developer put beside it, and a
/// reset that deletes a directory tree deletes all of that too. The database file,
/// its two SQLite sidecars, and the settings file are the whole of first-run
/// state.</para>
/// </summary>
internal static class LocalDataReset
{
    /// <summary>The per-user folder name, mirroring
    /// <c>WorkspaceSettingsStore.DefaultAppDataFolderName</c> — <c>#if DEBUG</c>
    /// included. A Release AppHost drives a Release harness, and that harness reads
    /// <c>Backlog</c>; resetting <c>Backlog.Debug</c> from underneath it would report
    /// success against a store nothing is reading and leave the live one untouched.
    ///
    /// <para>It is a copy rather than a project reference deliberately, and not
    /// because the reference is impossible — <c>Backlog.Infrastructure.FileSystem</c>
    /// and <c>Backlog.Infrastructure.Sqlite</c> are both plain <c>net10.0</c>. Aspire
    /// reads an AppHost's <c>ProjectReference</c>s as the project resources of the app
    /// model unless each one is marked otherwise, so referencing infrastructure here
    /// would pull a slice of the application graph into the host to share three
    /// constants. The price of the copy is that it has to stay in step with those two
    /// files, which is what <c>AspireAppModelTests</c> asserts.</para></summary>
#if DEBUG
    private const string WorkspaceFolderName = "Backlog.Debug";
#else
    private const string WorkspaceFolderName = "Backlog";
#endif

    private const string SettingsFileName = "settings.json";

    private const string DatabaseFileName = "backlog.db";

    /// <summary>The database and the two files SQLite keeps beside it. Leaving a
    /// write-ahead log behind when the database goes leaves the next run reading a
    /// journal for a database that no longer exists.</summary>
    private static readonly string[] DatabaseFileSuffixes = ["", "-wal", "-shm"];

    /// <summary>The per-user workspace folder, before any settings pointer is
    /// followed.</summary>
    private static string DefaultWorkspace => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        WorkspaceFolderName);

    /// <summary>The folder a reset would delete from: the default workspace, or
    /// wherever that workspace's settings point instead.
    ///
    /// <para>The app model calls this while it is being built, so the confirmation
    /// dialog can name the real folder. The settings screen accepts any rooted path,
    /// so "the <c>Backlog.Debug</c> workspace" is not a safe thing to promise someone
    /// before deleting a database — the path is.</para></summary>
    public static string ResolveRoot()
    {
        var workspace = DefaultWorkspace;

        return ConfiguredRoot(Path.Combine(workspace, SettingsFileName)) ?? workspace;
    }

    /// <param name="approvedRoot">The folder named in the confirmation the person
    /// approved. Re-resolved below and compared rather than trusted: settings can be
    /// repointed while the AppHost is running, and deleting a database in a folder
    /// nobody was shown is the one outcome this command must not have.</param>
    public static ExecuteCommandResult Run(ExecuteCommandContext context, string approvedRoot)
    {
        var workspace = DefaultWorkspace;

        var settings = Path.Combine(workspace, SettingsFileName);

        // The settings file is the pointer, so it decides where the database is;
        // removing it is what puts the pointer back to the default folder.
        var root = ConfiguredRoot(settings) ?? workspace;

        if (!SameFolder(root, approvedRoot))
        {
            return CommandResults.Failure(
                $"The workspace moved after this AppHost started: the confirmation named {approvedRoot}, but the "
                + $"settings now point at {root}. Nothing was deleted. Restart the AppHost so the confirmation "
                + "names the folder it would actually reset, then run this again.");
        }

        var targets = DatabaseFileSuffixes
            .Select(suffix => Path.Combine(root, DatabaseFileName + suffix))
            .Append(settings)
            .Where(File.Exists)
            .ToList();

        if (targets.Count == 0)
        {
            return CommandResults.Success($"Nothing to reset: {root} is already at first-run state.");
        }

        var removed = new List<string>();

        foreach (var target in targets)
        {
            try
            {
                File.Delete(target);
                removed.Add(Path.GetFileName(target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Something still holds the database open — a harness, a head, or
                // another worktree's run. Stop rather than carry on: half a store
                // removed is a worse state than the one the reset was asked for.
                return CommandResults.Failure(
                    $"Could not remove {target}: {ex.Message} Stop every resource that reads the workspace, "
                    + "then run this again.");
            }
        }

        context.Logger.LogInformation(
            "Reset the local workspace at {Root}: removed {Files}.", root, string.Join(", ", removed));

        return CommandResults.Success($"Reset {root} — removed {string.Join(", ", removed)}.");
    }

    /// <summary>Where the workspace settings say the backlog lives, or
    /// <c>null</c> when nothing has moved it. A settings file that cannot be read
    /// is treated as one that never moved anything: guessing at a root from a
    /// corrupt pointer is how a reset reaches a folder nobody named.</summary>
    private static string? ConfiguredRoot(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return null;

            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));

            return document.RootElement.TryGetProperty("rootDirectory", out var root)
                   && root.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(root.GetString())
                ? root.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // UnauthorizedAccessException is not an IOException, and it is what
            // File.ReadAllText throws for a settings file the AppHost may not read.
            // Without it the throw leaves the command callback entirely and the
            // dashboard shows an unhandled failure rather than a result.
            return null;
        }
    }

    /// <summary>Whether two paths name the same folder, ignoring a trailing
    /// separator and — this being a Windows development host — letter case. A path
    /// that cannot be resolved at all is reported as a mismatch, because the one
    /// thing it must not do is pass for the folder someone approved.</summary>
    private static bool SameFolder(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
