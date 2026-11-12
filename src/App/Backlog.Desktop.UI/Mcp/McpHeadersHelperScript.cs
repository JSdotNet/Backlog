namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The headers helper, <c>tools/mcp/backlog-mcp-headers.mjs</c> (local ADR 0052 §3), embedded in
/// this assembly and written to <c>&lt;app data&gt;\hooks\backlog-mcp-headers.mjs</c> on every start
/// beside the question hook, so a registration outside this repository — Claude Code's user
/// scope, the telemetry exporter — can run it by an absolute path. A repository's own
/// <c>.mcp.json</c> runs its checked-in copy instead.
/// </summary>
public static class McpHeadersHelperScript
{
    /// <summary>The script's file name, as it is installed.</summary>
    public const string FileName = "backlog-mcp-headers.mjs";

    /// <summary>Whether this is a Debug build, whose broker listens on a pipe of its own and
    /// whose helper therefore runs with <c>--debug</c>: its app-data folder is not the installed
    /// app's <c>Backlog</c>.</summary>
    public static bool IsDebugBuild { get; } =
        !string.Equals(Backlog.Infrastructure.FileSystem.WorkspaceSettingsStore.DefaultAppDataFolderName, "Backlog", StringComparison.Ordinal);

    /// <summary>The embedded resource's logical name, set in the project file.</summary>
    internal const string ResourceName = "Backlog.Desktop.UI.Mcp." + FileName;

    private static readonly Lazy<byte[]> Script = new(ReadScript);

    /// <summary>The script's bytes, exactly as embedded.</summary>
    public static ReadOnlyMemory<byte> Bytes => Script.Value;

    /// <summary>Where <see cref="Install"/> writes the script under <paramref name="appDataDirectory"/>:
    /// the question hook's folder.</summary>
    public static string PathUnder(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        return Path.Combine(appDataDirectory, AskBacklogHook.FolderName, FileName);
    }

    /// <summary>Writes the script under <paramref name="appDataDirectory"/> the way
    /// <see cref="AskBacklogHook.Install"/> writes its own, and answers its path.</summary>
    public static string Install(string appDataDirectory) => AskBacklogHook.Write(PathUnder(appDataDirectory), Script.Value);

    /// <summary>
    /// The command line that runs the installed script for <paramref name="grantId"/>: node on the
    /// absolute path in forward slashes inside double quotes, the form the question hooks use so
    /// every shell Claude Code may run it through hands node the same path, and <c>--debug</c>
    /// for a Debug build, whose broker listens on a pipe of its own.
    /// </summary>
    public static string Command(string scriptPath, string grantId, bool debug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);

        return $"node \"{scriptPath.Replace('\\', '/')}\" --grant {grantId}{(debug ? " --debug" : string.Empty)}";
    }

    private static byte[] ReadScript()
    {
        using var stream = typeof(McpHeadersHelperScript).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The {FileName} resource is missing from {typeof(McpHeadersHelperScript).Assembly.GetName().Name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
