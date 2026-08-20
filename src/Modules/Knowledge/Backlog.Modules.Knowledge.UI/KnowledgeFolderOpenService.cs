using Backlog.Modules.Knowledge.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Knowledge;

public sealed class KnowledgeFolderOpenService(IKnowledgeFolderSource source, IFolderEditorLauncher launcher)
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

        try
        {
            await launcher.OpenFolderAsync(folderPath, cancellationToken).ConfigureAwait(false);
        }
        catch (FolderEditorLaunchException ex)
        {
            // The launcher is domain-neutral and lives in the file-system adapter,
            // so it fails in its own vocabulary. Everything a knowledge screen can
            // show the user arrives as one exception type, and the launcher's
            // message is already the sentence to show — so it is carried across
            // verbatim rather than restated, with the original kept as the cause.
            throw new KnowledgeFolderOpenException(ex.Message, ex);
        }
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

/// <summary>
/// The one failure a knowledge screen has to handle: everything
/// <see cref="KnowledgeFolderOpenService"/> can go wrong on — an unconfigured
/// area, a folder that is not there, a path outside the knowledge root, and the
/// editor launch itself — arrives as this, so the panes catch one type and show
/// its message.
/// </summary>
public sealed class KnowledgeFolderOpenException : Exception
{
    public KnowledgeFolderOpenException(string message) : base(message) { }

    public KnowledgeFolderOpenException(string message, Exception innerException) : base(message, innerException) { }
}
