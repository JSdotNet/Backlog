using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Org ADR 0002: configuration binds to options classes with validation and
/// <c>ValidateOnStart()</c>, so a service with a bad setting fails when it starts rather than
/// on the first request that reads it.
///
/// <para>A source scan, the way <see cref="ProcessLaunchTests"/> is one: the bindings live in
/// the sync service, the infrastructure adapters and the desktop composition, and several of
/// them run only in one environment, which no single host started in a test would reach.
/// Each <c>AddOptions&lt;T&gt;()</c> is followed through its own call chain and every later
/// statement on the variable it was assigned to, so a binding split across statements —
/// <c>var owner = …AddOptions&lt;OwnerOptions&gt;()…; owner.Validate(…); owner.ValidateOnStart();</c>
/// — is read as one.</para>
/// </summary>
public partial class OptionsValidationTests
{
    /// <summary>Bindings the scan must find, so a scanner that stops matching anything fails
    /// here rather than passing on an empty list.</summary>
    private static readonly string[] KnownBoundOptions =
        ["CosmosOptions", "SpecManagerOptions", "SyncTokenOptions", "OwnerOptions", "GitHubOAuthOptions", "ConnectorOptions"];

    [Fact]
    public void Every_options_class_bound_from_configuration_is_validated_on_start()
    {
        var bindings = Bindings().Where(binding => binding.IsBound).ToList();

        Assert.All(KnownBoundOptions, known => Assert.Contains(bindings, binding => binding.Type == known));

        var offenders = bindings
            .Where(binding => !binding.IsValidated || !binding.ValidatesOnStart)
            .Select(binding => $"{binding.File}:{binding.Line} {binding.Type}"
                               + (binding.IsValidated ? "" : " (no Validate or ValidateDataAnnotations)")
                               + (binding.ValidatesOnStart ? "" : " (no ValidateOnStart)"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These options classes are bound from configuration without validation that runs at start "
            + "(org ADR 0002). Chain a Validate or ValidateDataAnnotations call and ValidateOnStart() onto "
            + "the binding:\n" + string.Join('\n', offenders));
    }

    /// <summary><c>services.Configure&lt;T&gt;(configuration)</c> binds with no way to validate,
    /// so it is the one binding the rule above cannot see and the one it refuses outright.</summary>
    [Fact]
    public void No_options_class_is_bound_through_configure_with_a_configuration_section()
    {
        var offenders = Sources()
            .SelectMany(source => ConfigureFromConfiguration().Matches(File.ReadAllText(source.FullName))
                .Select(match => $"{Relative(source)}: {match.Value}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Configure<T>(IConfiguration) binds options without validation. Use "
            + "AddOptions<T>().Bind(…)/BindConfiguration(…) with validation and ValidateOnStart():\n"
            + string.Join('\n', offenders));
    }

    private static IEnumerable<Binding> Bindings()
    {
        foreach (var source in Sources())
        {
            var text = File.ReadAllText(source.FullName);

            foreach (Match match in AddOptions().Matches(text))
            {
                var methods = new HashSet<string>(StringComparer.Ordinal);
                methods.UnionWith(Methods(Statement(text, match.Index)));

                if (match.Groups["onStart"].Success) methods.Add("ValidateOnStart");

                var variable = AssignedVariable(text, match.Index);
                if (variable is not null)
                {
                    // Followed only until the name is declared again, so a later method's own
                    // `var options` cannot lend this binding a validation it does not have.
                    // Not the next binding: OwnerEnrolmentRegistration binds two classes and
                    // validates both after the second.
                    var name = Regex.Escape(variable);
                    var from = match.Index + match.Length;
                    var redeclared = new Regex($@"\bvar\s+{name}\b").Match(text, from);
                    var scope = text[..(redeclared.Success ? redeclared.Index : text.Length)];

                    foreach (Match use in new Regex($@"\b{name}\s*\.").Matches(scope, from))
                    {
                        methods.UnionWith(Methods(Statement(scope, use.Index)));
                    }
                }

                yield return new Binding(
                    Relative(source),
                    text[..match.Index].Count(character => character == '\n') + 1,
                    match.Groups["type"].Value,
                    IsBound: methods.Contains("Bind") || methods.Contains("BindConfiguration"),
                    IsValidated: methods.Any(method => method.StartsWith("Validate", StringComparison.Ordinal) && method != "ValidateOnStart"),
                    ValidatesOnStart: methods.Contains("ValidateOnStart"));
            }
        }
    }

    /// <summary>The text from <paramref name="start"/> to the end of its statement: the first
    /// semicolon outside a string literal and outside the parentheses and braces of a lambda
    /// argument, since a validation message or a block lambda may carry one of its own.</summary>
    private static string Statement(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var verbatim = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];

            if (inString)
            {
                if (verbatim && character == '"' && index + 1 < text.Length && text[index + 1] == '"') index++;
                else if (!verbatim && character == '\\') index++;
                else if (character == '"') inString = false;
                continue;
            }

            // A comment is skipped whole: its apostrophes and semicolons are prose.
            if (character == '/' && index + 1 < text.Length && text[index + 1] is '/' or '*')
            {
                var close = text[index + 1] == '/' ? text.IndexOf('\n', index) : text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (close < 0) return text[start..];
                index = close;
                continue;
            }

            switch (character)
            {
                // A char literal, '"' or '\'' included, is skipped whole.
                case '\'':
                    index = text.IndexOf('\'', index + (text[index + 1] == '\\' ? 3 : 2));
                    if (index < 0) return text[start..];
                    break;
                case '"':
                    inString = true;
                    verbatim = index > 0 && (text[index - 1] == '@' || (text[index - 1] == '$' && index > 1 && text[index - 2] == '@'));
                    break;
                case '(' or '{': depth++; break;
                case ')' or '}': depth--; break;
                case ';' when depth <= 0: return text[start..index];
            }
        }

        return text[start..];
    }

    /// <summary>The variable the statement holding <paramref name="index"/> assigns, read
    /// back to the end of the statement before it.</summary>
    private static string? AssignedVariable(string text, int index)
    {
        var begin = text.LastIndexOfAny([';', '{', '}'], index) + 1;
        var assignment = Assignment().Match(text[begin..index]);
        return assignment.Success ? assignment.Groups["name"].Value : null;
    }

    private static IEnumerable<string> Methods(string statement) =>
        MethodCall().Matches(statement).Select(call => call.Groups["name"].Value);

    private static IEnumerable<FileInfo> Sources() =>
        new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');

    [GeneratedRegex(@"\bAddOptions(?<onStart>WithValidateOnStart)?<(?<type>[\w.]+)(?:<[^>]*>)?>\s*\(")]
    private static partial Regex AddOptions();

    [GeneratedRegex(@"\bvar\s+(?<name>\w+)\s*=")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\.\s*(?<name>\w+)\s*[<(]")]
    private static partial Regex MethodCall();

    /// <summary><c>Configure&lt;T&gt;(x)</c> with one argument that is not a lambda: the
    /// overload that takes an <c>IConfiguration</c>, whatever the section is called.</summary>
    [GeneratedRegex(@"\bConfigure<[\w.]+>\(\s*(?![\w\s]*=>)[\w.]+(?:\([^()]*\))?\s*\)")]
    private static partial Regex ConfigureFromConfiguration();

    private sealed record Binding(string File, int Line, string Type, bool IsBound, bool IsValidated, bool ValidatesOnStart);
}
