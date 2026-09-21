using System.Text.Json;
using Backlog.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Brings the app's per-user state out of the folder a packaged install used
/// to be redirected into, the first time the app starts writing where it says
/// it writes.
/// <para>
/// A packaged Windows app has its <c>%LocalAppData%</c> writes redirected into
/// <c>Packages\&lt;family&gt;\LocalCache\Local</c> unless its manifest says
/// otherwise. Ours did not say otherwise, so every install before the manifest
/// changed kept the backlog in a folder no screen ever named, while the
/// Storage page pointed at one that did not exist. The manifest now opts out,
/// which makes the named folder the real one — and empty, on a machine that
/// has been using the app. This is what fills it: the database, the folders
/// the app keeps beside it, and the per-device settings files, copied from
/// the redirected folder into the real one before anything reads either.
/// </para>
/// <para>
/// Copied, never moved, and never over something already there. The redirected
/// folder is left intact for the same reason a moved root is: the copy that
/// rescues somebody's data must not be the step that could lose it. And only
/// the app's own files come — what the person kept beside them in the days of
/// one markdown file per entry is theirs, and stays.
/// </para>
/// <para>
/// The settings file is copied last and is what makes the next start say
/// "already in place". Copying it first would let a database copy that failed
/// halfway leave the app pointed at an empty folder it believed was adopted;
/// copying it last means a failure is retried on the next start instead.
/// </para>
/// </summary>
public static class PackagedAppData
{
    /// <summary>The settings file's name, which the workspace store owns and
    /// this must agree with: it is both the per-device file that says where
    /// the root is and the marker that adoption is done.</summary>
    private const string SettingsFileName = "settings.json";

    /// <summary>Adopts what <paramref name="redirectedAppData"/> holds into
    /// <paramref name="appData"/>, and says what came of it. Never throws for
    /// a file-system reason: the app has to start either way, and Settings has
    /// to be reachable to say where things are.</summary>
    public static AppDataAdoption Adopt(string redirectedAppData, string appData)
    {
        if (!Directory.Exists(redirectedAppData)) return AppDataAdoption.NothingToAdopt;

        var settings = Path.Combine(appData, SettingsFileName);
        var database = SqliteTaskRepository.DatabasePathFor(appData);

        // The settings file is the marker adoption writes last, and a database
        // with entries in it is a backlog somebody is using. An empty database
        // is neither: it is what the app creates when it opens its store on the
        // start whose adoption just failed, and reading it as "in place" would
        // turn the failure that start reported as retried into one never
        // retried, with the real backlog left behind in the redirected folder.
        if (File.Exists(settings)) return AppDataAdoption.AlreadyInPlace;
        if (File.Exists(database) && !SqliteDatabaseFile.IsEmpty(database)) return AppDataAdoption.AlreadyInPlace;

        try
        {
            var copied = 0;

            // The root's contents first, and only when the root was the default
            // folder: a root pointed somewhere else is a real path that was
            // never redirected, and whatever backlog is still sitting in the
            // redirected folder beside its settings is one the app stopped
            // reading when the root moved.
            if (RootIsDefault(Path.Combine(redirectedAppData, SettingsFileName), redirectedAppData, appData))
            {
                var redirectedDatabase = SqliteTaskRepository.DatabasePathFor(redirectedAppData);
                if (File.Exists(redirectedDatabase))
                {
                    // The empty file from the failed start is taken out of the
                    // copy's way; the copy refuses an existing destination on
                    // purpose, and this is the one existing destination that
                    // holds nothing.
                    if (File.Exists(database)) SqliteDatabaseFile.Delete(database);
                    SqliteDatabaseFile.CopyTo(redirectedDatabase, database);
                    copied++;
                }

                WorkspaceSettingsStore.CopyOwnedRootFolders(redirectedAppData, appData);
            }

            // Then the per-device files: one JSON file per store at the top of
            // the folder, which is the convention every store under Workspace/
            // and the sync client follow. Caches are not among them and are not
            // carried — a snapshot or an activity index is rebuilt from where it
            // came, and a cache folder can be large.
            Directory.CreateDirectory(appData);
            foreach (var file in Directory.EnumerateFiles(redirectedAppData, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, SettingsFileName, StringComparison.OrdinalIgnoreCase)) continue;

                var target = Path.Combine(appData, name);
                if (File.Exists(target)) continue;

                File.Copy(file, target);
                copied++;
            }

            var redirectedSettings = Path.Combine(redirectedAppData, SettingsFileName);
            if (File.Exists(redirectedSettings))
            {
                File.Copy(redirectedSettings, settings);
                copied++;
            }

            return copied == 0 ? AppDataAdoption.NothingToAdopt : AppDataAdoption.Adopted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or SqliteException)
        {
            return AppDataAdoption.Failed(ex.Message);
        }
    }

    /// <summary>Whether the settings file names the default folder as the
    /// root, or names nothing — the store reads both as the default. The file
    /// names the path the app saw, which is the real one, while it sits at the
    /// redirected one; either spelling is the same folder. Read loosely rather
    /// than through the store's own type: a settings file that does not parse
    /// is not a reason to leave the database beside it behind, and the store
    /// has its own answer for that file once it is in place.</summary>
    private static bool RootIsDefault(string settingsPath, string redirectedAppData, string appData)
    {
        if (!File.Exists(settingsPath)) return true;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "rootDirectory", StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind != JsonValueKind.String) return true;

                var root = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(root)) return true;

                return SameFolder(root, appData) || SameFolder(root, redirectedAppData);
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool SameFolder(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
