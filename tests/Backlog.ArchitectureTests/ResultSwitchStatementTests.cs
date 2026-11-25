using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A <c>Result</c> is read with an exhaustive <c>switch</c> expression, because CS8509 is a
/// build error here and proves every case handled (app-defaults ADR 0005). A <c>switch</c>
/// statement with <c>case Ok&lt;T&gt;:</c> / <c>case Error:</c> labels compiles without that
/// check, so a case added to the union would pass it unnoticed — which is how six sync
/// workers, the notification sync session and the connector settings page read one until they
/// were rewritten as switch expressions over a local function per arm.
/// </summary>
public partial class ResultSwitchStatementTests
{
    [Fact]
    public void No_source_file_matches_a_result_case_in_a_switch_statement()
    {
        var src = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));
        var files = Repository.SourceFilesUnder(src, ".cs", ".razor").ToList();

        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(file => File.ReadAllLines(file.FullName)
                .Select((line, index) => (line, index))
                .Where(entry => ResultCaseLabel().IsMatch(entry.line))
                .Select(entry => $"{Repository.RelativePath(file)}:{entry.index + 1}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These switch statements match a Result case, which CS8509 does not check. Rewrite each as a "
            + "switch expression — a local function per arm when an arm runs several statements:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>A <c>case</c> label on <c>Ok</c>, <c>Ok&lt;T&gt;</c> or <c>Error</c>, with any
    /// type argument, nested ones included, a designation or a positional pattern — not
    /// <c>Error.None</c>, <c>ErrorType.X</c> or another name that starts the same way.</summary>
    [GeneratedRegex(@"^\s*case\s+(?:Ok|Error)\b(?!\.)")]
    private static partial Regex ResultCaseLabel();
}
