namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The question relay's hook script, <c>ask-backlog.mjs</c> (local ADR 0041): embedded in this
/// assembly, served at <c>GET /hooks/ask-backlog.mjs</c> on the MCP listener, and written to
/// <c>&lt;app data&gt;\hooks\ask-backlog.mjs</c> on every start, so the hook a session runs is
/// always the running app's own.
/// <para>
/// Here rather than in the MAUI head for the reason <see cref="SessionRequestWaitEndpoint"/>
/// gives: both hosts map the route, and no test project can reference the head.
/// </para>
/// </summary>
public static class AskBacklogHook
{
    /// <summary>The script's file name, as it is served and installed.</summary>
    public const string FileName = "ask-backlog.mjs";

    /// <summary>Where the route is served, under the listener's address.</summary>
    public const string RoutePath = "/hooks/" + FileName;

    /// <summary>The folder under the app data directory the script is written to.</summary>
    public const string FolderName = "hooks";

    /// <summary>What the route answers with.</summary>
    public const string ContentType = "text/javascript; charset=utf-8";

    /// <summary>The embedded resource's logical name, set in the project file.</summary>
    internal const string ResourceName = "Backlog.Desktop.UI.Mcp." + FileName;

    private static readonly Lazy<byte[]> Script = new(ReadScript);

    /// <summary>The script's bytes, exactly as embedded.</summary>
    public static ReadOnlyMemory<byte> Bytes => Script.Value;

    /// <summary>
    /// Writes the script to <c>hooks\ask-backlog.mjs</c> under <paramref name="appDataDirectory"/>
    /// and answers its path. A file already holding the same bytes is left alone; a different one
    /// is replaced through a temporary file, so a hook starting meanwhile reads the old script or
    /// the new one and never half of each.
    /// </summary>
    public static string Install(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        var folder = Path.Combine(appDataDirectory, FolderName);
        var target = Path.Combine(folder, FileName);
        var bytes = Script.Value;

        if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) return target;

        Directory.CreateDirectory(folder);

        var temporary = Path.Combine(folder, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return target;
    }

    private static byte[] ReadScript()
    {
        using var stream = typeof(AskBacklogHook).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The {FileName} resource is missing from {typeof(AskBacklogHook).Assembly.GetName().Name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
