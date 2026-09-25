using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>annotation</c> fences as the rule writes them, for the tests that draw
/// them and the tests that must not read them as chapter content.
///
/// <para>Lifted out of the vendored <c>devbook-annotations.md</c> the way
/// <c>DevbookAnnotationFenceTests</c> lifts them, so the shapes are the rule's and
/// not this file's. The one sample the rule does not carry — a note whose body
/// quotes a code sample — is the rule's own three-line note with its body swapped
/// for a block scalar holding one, and opened with four backticks, which is how a
/// fence that holds a fence is written.</para>
/// </summary>
internal static class DevbookAnnotationSamples
{
    /// <summary>The bodies of the rule's two examples, in order: the three-line
    /// note, then the resolved question with a quote and a reply.</summary>
    public static IReadOnlyList<string> RuleExamples { get; } =
    [.. Regex.Matches(DevbookRuleText.Annotations.Replace("\r\n", "\n"), "```annotation\n(.*?)\n```", RegexOptions.Singleline)
        .Select(match => match.Groups[1].Value)];

    /// <summary>The three-line note, as a whole fence.</summary>
    public static string ThreeLineNote => Fence(RuleExamples[0]);

    /// <summary>The full thread, as a whole fence.</summary>
    public static string FullThread => Fence(RuleExamples[1]);

    /// <summary>The line a note with a code sample says before the sample, so a
    /// test can find the note's own text wherever it ended up.</summary>
    public const string CodeSampleLead = "Should this read the file before the lock?";

    /// <summary>The line inside the quoted code sample.</summary>
    public const string CodeSampleLine = "var text = File.ReadAllText(path);";

    /// <summary>The line after the sample, which a reader closing the note at the
    /// sample's own <c>```</c> would have spilled into the chapter.</summary>
    public const string CodeSampleTail = "Otherwise the lock covers nothing.";

    /// <summary>The rule's three-line note with a body that quotes a code
    /// sample, opened with four backticks so the sample's three can sit inside
    /// it.</summary>
    public static string NoteWithCodeSample
    {
        get
        {
            var header = string.Join('\n', RuleExamples[0].Split('\n').Where(line => !line.StartsWith("body:", StringComparison.Ordinal)));

            return $"""
                ````annotation
                {header}
                body: |
                  {CodeSampleLead}
                  ```csharp
                  {CodeSampleLine}
                  ```
                  {CodeSampleTail}
                ````
                """.Replace("\r\n", "\n");
        }
    }

    private static string Fence(string body) => $"```annotation\n{body}\n```";
}
