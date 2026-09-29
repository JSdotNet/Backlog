namespace Backlog.ArchitectureTests;

/// <summary>
/// The Inbox's settings section is on the settings screen only because a host
/// registered it: <c>AddInboxSettings</c> (see <c>InboxSettingsRegistration</c> in
/// <c>Backlog.Modules.Inbox.UI</c>) is the one line that puts the routing rules
/// there, and the shell holds no copy of its own to fall back on. A host that
/// composes the Inbox and leaves the call out builds, starts and shows every
/// other settings page — the rules are simply gone.
///
/// <para>
/// The two desktop heads — <c>src/App/Backlog.Desktop/MauiProgram.cs</c> and the
/// desktop web harness — must not drift on it, and a MAUI head cannot be composed
/// in a test process. So this is a text scan, the same shape and the same limits as
/// <see cref="TaskSyncClientRegistrationTests"/>: a composition root that wires the
/// Inbox into the desktop shell (it names <c>AddInboxAiContentSource(</c>) must also
/// name <c>AddInboxSettings(</c>. It cannot tell whether the line runs.
/// </para>
/// </summary>
public class InboxSettingsRegistrationTests
{
    private static readonly string[] Roots = ["src/App", "src/Harness"];

    [Fact]
    public void Every_composition_root_that_composes_the_desktop_inbox_registers_its_settings_section()
    {
        var files = CompositionRoots().ToList();

        var inboxHosts = files
            .Where(file => file.Text.Contains("AddInboxAiContentSource(", StringComparison.Ordinal))
            .ToList();

        // Both desktop heads compose the Inbox today; an empty list would make the
        // rule below pass for the wrong reason.
        Assert.Contains(inboxHosts, file => file.RelativePath == "src/App/Backlog.Desktop/MauiProgram.cs");
        Assert.Contains(inboxHosts, file => file.RelativePath == "src/Harness/Backlog.Desktop.WebHarness/Program.cs");

        var offenders = inboxHosts
            .Where(file => !file.Text.Contains("AddInboxSettings(", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These composition roots put the Inbox in the desktop shell without registering its settings "
            + "section, so the routing rules are missing from their settings screen. Call "
            + "AddInboxSettings() beside AddInboxAiContentSource():\n" + string.Join('\n', offenders));
    }

    /// <summary>Every <c>MauiProgram.cs</c> or <c>Program.cs</c> under the app heads
    /// and the harnesses.</summary>
    private static IEnumerable<(string RelativePath, string Text)> CompositionRoots()
    {
        foreach (var root in Roots)
        {
            var folder = new DirectoryInfo(Path.Combine([Repository.Root.FullName, .. root.Split('/')]));
            if (!folder.Exists) continue;

            foreach (var file in folder.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    continue;

                if (file.Name is not ("MauiProgram.cs" or "Program.cs")) continue;

                yield return (
                    Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/'),
                    File.ReadAllText(file.FullName));
            }
        }
    }
}
