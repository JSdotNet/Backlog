using System.Text;

using Backlog.Desktop.UI.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The question hook script as the app carries it (local ADR 0041): embedded, and written to
/// <c>&lt;app data&gt;\hooks</c> on every start so the installed hook is the running app's own.
/// </summary>
public sealed class AskBacklogHookTests : IDisposable
{
    private readonly string _appData = Path.Combine(Path.GetTempPath(), "ask-backlog-hook-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_appData)) Directory.Delete(_appData, recursive: true);
    }

    [Fact]
    public void The_script_is_embedded_as_a_node_script()
    {
        var text = Encoding.UTF8.GetString(AskBacklogHook.Bytes.Span);

        Assert.StartsWith("#!/usr/bin/env node", text, StringComparison.Ordinal);
        Assert.Contains("tools/call", text, StringComparison.Ordinal);
        Assert.Equal("/hooks/ask-backlog.mjs", AskBacklogHook.RoutePath);
    }

    [Fact]
    public void Install_writes_the_embedded_bytes_to_the_hooks_folder()
    {
        var path = AskBacklogHook.Install(_appData);

        Assert.Equal(Path.Combine(_appData, "hooks", "ask-backlog.mjs"), path);
        Assert.Equal(AskBacklogHook.Bytes.ToArray(), File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(Path.Combine(_appData, "hooks")));
    }

    /// <summary>An older app's script is replaced on the next start.</summary>
    [Fact]
    public void Install_replaces_a_script_an_earlier_version_left()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_appData, "hooks")).FullName;
        File.WriteAllText(Path.Combine(folder, "ask-backlog.mjs"), "// an older app's script");

        var path = AskBacklogHook.Install(_appData);

        Assert.Equal(AskBacklogHook.Bytes.ToArray(), File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(folder));
    }

    /// <summary>A script that already matches is left alone, so a hook running it is never
    /// raced by a rewrite of the same bytes.</summary>
    [Fact]
    public void Install_leaves_a_matching_script_untouched()
    {
        var path = AskBacklogHook.Install(_appData);
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        AskBacklogHook.Install(_appData);

        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }
}
