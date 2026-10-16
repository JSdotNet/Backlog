using System.Text.Json;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The Brain folder setting (<c>brain-folder</c>), kept in its own per-user file
/// beside the workspace's <c>settings.json</c>.
/// <para>
/// Beside it rather than inside it, and never in the Brain folder itself. Never in
/// the folder, because local ADR 0026 says so: the app has to know the path before
/// it can read the folder, the same reason <see cref="WorkspaceSettingsStore"/>
/// keeps the backlog's own location out of the backlog. A file of its own rather
/// than one more key in the workspace's, because that file is one decision — this
/// folder, backed up to that repository — and the Brain is not part of it: moving
/// the backlog must not move the Brain, and the Devbook context owns the setting.
/// </para>
/// <para>
/// No default value. <see cref="BrainFolder"/> is null until somebody picks a
/// folder, and a file that cannot be read reads as that same nothing rather than
/// stopping the app from opening.
/// </para>
/// </summary>
public sealed class BrainSettingsStore : IBrainSettings
{
    /// <summary>The file's name beside <c>settings.json</c>.</summary>
    public const string FileName = "brain.settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    /// <summary>The installed app's file, in the same per-user folder as the
    /// workspace settings.</summary>
    public BrainSettingsStore()
        : this(Path.Combine(WorkspaceSettingsStore.DefaultAppDataDirectory, FileName))
    {
    }

    /// <summary>A file of the caller's choosing, so a test or a harness session
    /// never rewrites the person's real setting.</summary>
    public BrainSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        _path = settingsPath;
        BrainFolder = Read();
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public string? BrainFolder { get; private set; }

    /// <summary>Where the setting is kept - shown nowhere, but what a test
    /// asserts the path landed in.</summary>
    public string SettingsPath => _path;

    /// <inheritdoc />
    public string? TrySetBrainFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BrainFolder is null ? null : Commit(null);
        }

        var trimmed = path.Trim();

        // Resolving against whatever the working directory happens to be would
        // put somebody's notes somewhere they never named.
        if (!Path.IsPathRooted(trimmed)) return "Use a full path, such as D:\\Notes\\Brain.";

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That is not a folder path.";
        }

        if (File.Exists(full)) return "That is a file. Pick a folder for the Brain.";

        if (RepositoryAbove(full) is { } repository)
        {
            return $"That folder is inside the repository at {repository}. The Brain lives outside every repository - pick a folder of its own.";
        }

        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Couldn't use that folder: {ex.Message}";
        }

        return string.Equals(full, BrainFolder, StringComparison.OrdinalIgnoreCase) ? null : Commit(full);
    }

    /// <summary>The working tree <paramref name="folder"/> sits in, or null. A
    /// <c>.git</c> folder or file — a linked worktree has the file — on the folder
    /// or any folder above it is what makes one; the folder itself need not exist
    /// yet.</summary>
    internal static string? RepositoryAbove(string folder)
    {
        try
        {
            for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
            {
                var marker = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(marker) || File.Exists(marker)) return current.FullName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A disk that will not say is not a repository this can name.
        }

        return null;
    }

    private string? Commit(string? folder)
    {
        var error = Save(folder);
        if (error is not null) return error;

        BrainFolder = folder;
        Changed?.Invoke();
        return null;
    }

    private string? Save(string? folder)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, JsonSerializer.Serialize(new BrainSettingsDto { BrainFolder = folder }, JsonOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't save the Brain folder: {ex.Message}";
        }
    }

    private string? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            var folder = JsonSerializer.Deserialize<BrainSettingsDto>(File.ReadAllText(_path), JsonOptions)?.BrainFolder;
            return string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed class BrainSettingsDto
    {
        public string? BrainFolder { get; init; }
    }
}
