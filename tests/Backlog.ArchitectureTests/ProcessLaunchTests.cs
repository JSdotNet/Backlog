namespace Backlog.ArchitectureTests;

/// <summary>
/// The desktop head is a GUI-subsystem process: it has no console of its own, so
/// Windows gives every child process it starts a brand new console window unless
/// the launch asks for <c>CREATE_NO_WINDOW</c>. Redirecting the child's streams
/// does not ask for it — <c>CreateNoWindow</c> is the only thing that does, and
/// it defaults to <c>false</c>.
///
/// <para>That is how the system tools pane came to flash a dozen console windows
/// across the screen every time it opened: it redirected both streams, read the
/// output, and never set the flag. Every other launcher in the repository had it
/// right, which is exactly why nobody noticed the one that did not.</para>
///
/// <para>The rule is a source scan rather than a unit test because the offending
/// file lives in <c>src/App/Backlog.Desktop</c>, the MAUI head, which no test
/// project references or can reference.</para>
/// </summary>
public class ProcessLaunchTests
{
    /// <summary>
    /// The launches that deliberately show a console, keyed on the file that
    /// writes them and carrying the reason.
    ///
    /// <para>An entry here is a claim that a window appearing is the feature
    /// rather than the bug. If the answer is instead "this one does not need a
    /// window either", the answer is <c>CreateNoWindow = true</c>, not a fourth
    /// line in this list.</para>
    ///
    /// <list type="bullet">
    /// <item><c>CopilotCliLauncher.cs</c> — launches the interactive Copilot CLI,
    /// where the terminal is the point. It is not redirected and not read: the
    /// window it opens is the thing the user was handed.</item>
    /// </list>
    /// </summary>
    private static readonly string[] AllowedConsoleWindows = ["CopilotCliLauncher.cs"];

    /// <summary>How far past the <c>new ProcessStartInfo</c> the object
    /// initializer is read before giving up looking for its closing brace. Every
    /// launcher in this repository writes the flag inside that initializer, so
    /// that is where the rule looks.</summary>
    private const int InitializerLimit = 30;

    [Fact]
    public void Every_process_launch_suppresses_its_console_window()
    {
        var launches = Launches().ToList();

        Assert.NotEmpty(launches);

        var offenders = launches
            .Where(launch => !IsAllowed(launch) && !SuppressesTheWindow(launch))
            .Select(launch => $"{launch.File}:{launch.Line}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These process launches leave CreateNoWindow unset, so each child process opens a console "
            + "window over the app. Add `CreateNoWindow = true` to the ProcessStartInfo, or — if the "
            + "window really is the feature — add the file to AllowedConsoleWindows with the reason:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// An exception that has stopped being one is worse than no exception list:
    /// it reads as a considered decision while quietly permitting anything.
    /// </summary>
    [Fact]
    public void Every_allowed_console_window_is_still_a_console_window()
    {
        var launches = Launches().ToList();

        Assert.NotEmpty(launches);

        var stale = AllowedConsoleWindows
            .Where(allowed => !launches.Any(launch =>
                launch.File.EndsWith(allowed, StringComparison.Ordinal) && !SuppressesTheWindow(launch)))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These console-window exceptions no longer match a launch that shows a console and should be "
            + "deleted: " + string.Join(", ", stale));
    }

    private static bool IsAllowed(Launch launch) =>
        AllowedConsoleWindows.Any(allowed => launch.File.EndsWith(allowed, StringComparison.Ordinal));

    /// <summary>Asks for the flag by value rather than by name: the launcher that
    /// wants a window writes <c>CreateNoWindow = false</c>, and a rule that only
    /// looked for the property would read that as compliance.</summary>
    private static bool SuppressesTheWindow(Launch launch) =>
        launch.Initializer.Contains("CreateNoWindow = true", StringComparison.Ordinal);

    private static IEnumerable<Launch> Launches()
    {
        foreach (var source in Sources())
        {
            var lines = File.ReadAllLines(source.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("new ProcessStartInfo", StringComparison.Ordinal)) continue;

                yield return new Launch(Relative(source), index + 1, Initializer(lines, index));
            }
        }
    }

    /// <summary>The object initializer that follows the constructor call, read as
    /// text up to its closing brace.</summary>
    private static string Initializer(IReadOnlyList<string> lines, int start)
    {
        var window = new List<string>();

        for (var index = start; index < lines.Count && window.Count < InitializerLimit; index++)
        {
            window.Add(lines[index]);

            if (lines[index].TrimEnd().EndsWith("};", StringComparison.Ordinal)) break;
        }

        return string.Join('\n', window);
    }

    private static IEnumerable<FileInfo> Sources() =>
        new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');

    private sealed record Launch(string File, int Line, string Initializer);
}
