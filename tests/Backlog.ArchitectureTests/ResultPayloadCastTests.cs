using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A <c>Result</c> is read with an exhaustive <c>switch</c> over its cases, never by casting the
/// held case out of the union's <c>object?</c> <c>Value</c> (app-defaults ADR 0005). A cast such
/// as <c>(Error)result.Value!</c> compiles whatever the union holds and throws when it guessed
/// wrong; <c>result switch { Ok&lt;T&gt;(var value) =&gt; …, Error error =&gt; … }</c> is checked
/// by CS8509, which is a build error here.
/// </summary>
public partial class ResultPayloadCastTests
{
    [Fact]
    public void No_source_file_casts_a_result_payload()
    {
        var src = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));
        var files = Repository.SourceFilesUnder(src, ".cs", ".razor").ToList();

        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(file => File.ReadAllLines(file.FullName)
                .Select((line, index) => (line, index))
                .Where(entry => PayloadCast().IsMatch(CodeOf(entry.line)))
                .Select(entry => $"{Repository.RelativePath(file)}:{entry.index + 1}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These lines cast a Result's payload, which no compiler check covers. Read the union with a "
            + "switch expression — `x switch { Ok<T>(var v) => …, Error e => … }`, the rest of the method "
            + "as a local function where a guard returned early:\n"
            + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("return (Error)result.Value!;")]
    [InlineData("var error = (Error)page.Value!;")]
    [InlineData("return ((Error)created.Value!).Message;")]
    [InlineData("var ok = (Ok<TaskItemDto>)found.Value!;")]
    [InlineData("var counts = (Ok<(int Pushed, bool Settled)>)pushed.Value!;")]
    [InlineData("var map = (Ok<Dictionary<string, List<int>>>)read.Value!;")]
    [InlineData("var done = (Ok)saved.Value!;")]
    [InlineData("@(((Error)result.Value!).Message)")]
    [InlineData("throw Failure((Error)captured.Value!);")]
    [InlineData("return NotYetOr( (Error) push.Value!);")]
    public void The_rule_catches_a_payload_cast(string line) =>
        Assert.Matches(PayloadCast(), CodeOf(line));

    [Theory]
    [InlineData("if (string.IsNullOrWhiteSpace(Error)) return;")]
    [InlineData("<p hidden=\"@string.IsNullOrEmpty(Error)\">")]
    [InlineData("return result switch { Ok<int>(var value) => value, Error error => 0 };")]
    [InlineData("if (result is Error error) return error;")]
    [InlineData("Result<int> Failed(Error error) => error;")]
    [InlineData("var types = new[] { typeof(Error), typeof(Ok<int>) };")]
    [InlineData("ErrorType type = (ErrorType)value;")]
    [InlineData("return (Error.None);")]
    [InlineData("var x = (Errors)y;")]
    [InlineData("// return (Error)result.Value!; is how this used to read")]
    [InlineData("Handle(Error) => x;")]
    public void The_rule_passes_a_line_that_casts_no_payload(string line) =>
        Assert.DoesNotMatch(PayloadCast(), CodeOf(line));

    /// <summary>The line with a trailing <c>//</c> comment cut off, so a comment that quotes
    /// a cast is not one.</summary>
    private static string CodeOf(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    /// <summary>A parenthesised <c>Error</c>, <c>Ok</c> or <c>Ok&lt;T&gt;</c> (nested and tuple
    /// type arguments included) followed by an operand — a cast. The lookbehind skips a
    /// parenthesis that opens an argument list, as in <c>string.IsNullOrWhiteSpace(Error)</c>
    /// or <c>typeof(Error)</c>.</summary>
    [GeneratedRegex(@"(?<![\w>\]\)])\(\s*(?:Error|Ok(?:<.+?>)?)\s*\)\s*[\w@(]")]
    private static partial Regex PayloadCast();
}
