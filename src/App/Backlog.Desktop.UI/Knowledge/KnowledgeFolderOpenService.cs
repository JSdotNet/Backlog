using Backlog.Desktop.UI.Workspace;
using System.ComponentModel;
using System.Diagnostics;

namespace Backlog.Desktop.UI.Knowledge;

public sealed class KnowledgeFolderOpenService(KnowledgeFolderSource source, IFolderEditorLauncher launcher)
{
    public async Task OpenAsync(string areaKey, string? nodePath, string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var location = source.Resolve(FolderKey(areaKey), repositoryAlias);
        if (!location.Available || string.IsNullOrWhiteSpace(location.FullPath))
        {
            throw new KnowledgeFolderOpenException(location.Message ?? $"{AreaLabel(areaKey)} knowledge is unavailable.");
        }

        var folderPath = ResolveFolderPath(location.FullPath, areaKey, nodePath);
        if (!Directory.Exists(folderPath))
        {
            throw new KnowledgeFolderOpenException($"The knowledge folder was not found at {folderPath}.");
        }

        await launcher.OpenFolderAsync(folderPath, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveFolderPath(string rootPath, string areaKey, string? nodePath)
    {
        if (string.IsNullOrWhiteSpace(nodePath) || IsAreaRoot(areaKey, nodePath))
        {
            return rootPath;
        }

        var relativePath = NormalizeNodePath(areaKey, nodePath);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!fullPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeFolderOpenException("The selected knowledge folder is outside the configured knowledge root.");
        }

        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }

        if (string.Equals(areaKey, "instructions", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(relativePath, ".agent", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(".agent/", StringComparison.OrdinalIgnoreCase)))
        {
            var fallbackRelativePath = string.Equals(relativePath, ".agent", StringComparison.OrdinalIgnoreCase)
                ? ".agents"
                : ".agents/" + relativePath[".agent/".Length..];
            var fallback = Path.GetFullPath(Path.Combine(rootPath, fallbackRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (Directory.Exists(fallback))
            {
                return fallback;
            }
        }
        return fullPath;
    }

    private static bool IsAreaRoot(string areaKey, string nodePath) => areaKey.ToLowerInvariant() switch
    {
        "backlog" => string.Equals(nodePath, ".backlog", StringComparison.OrdinalIgnoreCase),
        "domain" => string.Equals(nodePath, ".domain", StringComparison.OrdinalIgnoreCase),
        "arc42" => string.Equals(nodePath, ".arc42", StringComparison.OrdinalIgnoreCase),
        "tech" => string.Equals(nodePath, ".tech", StringComparison.OrdinalIgnoreCase),
        "design" => string.Equals(nodePath, ".design", StringComparison.OrdinalIgnoreCase),
        "instructions" => string.Equals(nodePath, "instructions", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static string NormalizeNodePath(string areaKey, string nodePath)
    {
        var normalized = nodePath.Replace('\\', '/').Trim('/');
        if (string.Equals(areaKey, "instructions", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var folderPrefix = FolderKey(areaKey).TrimStart('.') + "/";
        return normalized.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[folderPrefix.Length..]
            : normalized;
    }

    private static string FolderKey(string areaKey) => areaKey.ToLowerInvariant() switch
    {
        "backlog" => ".backlog",
        "domain" => ".domain",
        "arc42" => ".arc42",
        "tech" => ".tech",
        "design" => ".design",
        "instructions" => "instructions",
        _ => areaKey
    };

    private static string AreaLabel(string areaKey) => areaKey.ToLowerInvariant() switch
    {
        "backlog" => "Backlog",
        "domain" => "Domain",
        "arc42" => "Architecture",
        "tech" => "Technology",
        "design" => "Design",
        "instructions" => "Instructions",
        _ => areaKey
    };
}

public interface IFolderEditorLauncher
{
    Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default);
}

public sealed class VsCodeFolderEditorLauncher : IFolderEditorLauncher
{
    private const string EnvironmentVariable = "BACKLOG_VSCODE_CLI";
    private readonly string _executable;

    public VsCodeFolderEditorLauncher()
        : this(Environment.GetEnvironmentVariable(EnvironmentVariable))
    {
    }

    internal VsCodeFolderEditorLauncher(string? executable)
    {
        _executable = string.IsNullOrWhiteSpace(executable)
            ? OperatingSystem.IsWindows() ? "code.cmd" : "code"
            : executable.Trim();
    }

    public Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(folderPath)) throw new KnowledgeFolderOpenException($"The folder does not exist: {folderPath}");

        var startInfo = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(folderPath);

        try
        {
            if (Process.Start(startInfo) is null)
            {
                throw new KnowledgeFolderOpenException("VS Code did not start.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new KnowledgeFolderOpenException($"Couldn't open VS Code. Install the 'code' command or set {EnvironmentVariable} to the executable path.", ex);
        }

        return Task.CompletedTask;
    }
}

public sealed class UnsupportedFolderEditorLauncher : IFolderEditorLauncher
{
    public Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default) =>
        throw new KnowledgeFolderOpenException("Opening folders in VS Code is only available in the desktop app.");
}

public sealed class KnowledgeFolderOpenException : Exception
{
    public KnowledgeFolderOpenException(string message) : base(message) { }

    public KnowledgeFolderOpenException(string message, Exception innerException) : base(message, innerException) { }
}
