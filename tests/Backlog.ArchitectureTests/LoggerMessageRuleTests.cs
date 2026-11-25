using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Product log messages are <c>[LoggerMessage]</c> source-generated methods (org ADR 0007):
/// never a call to the <c>ILogger</c> extension methods, and never an interpolated message.
/// <para>
/// The build already rejects both — CA1848 and CA2254 are warnings under <c>src/</c> in the
/// root <c>.editorconfig</c>, and every build treats warnings as errors — but only for what
/// it compiles. The pull request build compiles neither MAUI head, and the Razor source
/// generator's trees match none of the <c>.editorconfig</c> globs, so this reads every C#
/// and Razor source under <c>src/</c> instead. It is a source scan because the shape is the
/// defect: a logger member followed by one of the extension method names, or a
/// <c>[LoggerMessage]</c> whose message is an interpolated string.
/// </para>
/// </summary>
public sealed partial class LoggerMessageRuleTests
{
    [Fact]
    public void No_product_source_calls_the_logger_extension_methods()
    {
        var offenders = Sources()
            .SelectMany(source => Matches(source, ExtensionCall()))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These lines log through the ILogger extension methods. Declare a [LoggerMessage] method on "
            + "the type instead — a private static partial void LogXxx(ILogger logger, ...) with a constant "
            + "message — the way src/Infrastructure/Backlog.Infrastructure.AzureFoundry/"
            + "AzureFoundryInboxRecordingTranscriber.cs does. In a Razor component, put it in the "
            + ".razor.cs code-behind, which the logging generator can see:\n"
            + string.Join('\n', offenders));
    }

    [Fact]
    public void No_logger_message_is_interpolated()
    {
        var offenders = Sources()
            .SelectMany(source => Matches(source, InterpolatedMessage()))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These [LoggerMessage] messages are interpolated strings. A message is a constant template "
            + "with {Placeholders}, and the values are the method's parameters:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>The scan must see the heads it exists for, or it is green for the wrong reason.</summary>
    [Fact]
    public void The_scan_reads_both_heads_and_the_razor_components()
    {
        var sources = Sources().Select(Relative).ToList();

        Assert.Contains(sources, path => path.StartsWith("src/App/Backlog.Desktop/", StringComparison.Ordinal));
        Assert.Contains(sources, path => path.StartsWith("src/App/Backlog.Mobile/", StringComparison.Ordinal));
        Assert.Contains(sources, path => path.EndsWith(".razor", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Matches(FileInfo source, Regex pattern)
    {
        var lines = File.ReadAllLines(source.FullName);

        for (var index = 0; index < lines.Length; index++)
        {
            if (pattern.IsMatch(lines[index]))
            {
                yield return $"{Relative(source)}:{index + 1}";
            }
        }
    }

    private static IEnumerable<FileInfo> Sources()
    {
        var src = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));

        return src.EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Concat(src.EnumerateFiles("*.razor", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file));
    }

    private static bool IsBuildOutput(FileInfo file) =>
        file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');

    /// <summary>A member access — <c>logger.</c>, <c>_log?.</c>, <c>Logger</c> on its own line —
    /// followed by an extension method's name and its opening parenthesis. A method of one's
    /// own called <c>LogSomething(</c> has no dot in front of it. The bare <c>Log(level, ...)</c>
    /// extension is told apart from a method of one's own named <c>Log</c>
    /// (<c>adoption.Log(logger)</c>) by its receiver, a logger, or by a level as its first
    /// argument.</summary>
    [GeneratedRegex(@"\??\.(LogTrace|LogDebug|LogInformation|LogWarning|LogError|LogCritical|BeginScope)\(|\??\.Log\(\s*(LogLevel\.|level\b)|(?i:log|logger)\??\.Log\(")]
    private static partial Regex ExtensionCall();

    [GeneratedRegex(@"\[LoggerMessage\([^\]]*Message\s*=\s*\$""")]
    private static partial Regex InterpolatedMessage();
}
