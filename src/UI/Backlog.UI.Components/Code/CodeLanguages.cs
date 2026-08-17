namespace Backlog.UI.Components.Code;

/// <summary>A language the highlighter knows, by the name a fenced code block
/// would use and by the name a person would read.</summary>
public sealed record CodeLanguage(string Id, string Label);

/// <summary>
/// The languages the highlighter colours, and the grammar for each. The list is
/// deliberately the set this product's own entries and docs are written in —
/// this is a backlog tool, not an IDE, and a language nobody pastes here is a
/// grammar nobody maintains. Anything not listed still renders: it just renders
/// plain, which is the correct outcome for a snippet the library cannot claim to
/// understand.
/// </summary>
public static class CodeLanguages
{
    /// <summary>Which of the four tokenizers reads a language. Most of them are
    /// the C-like one under different keywords and comment markers: SQL and
    /// shell differ from C# in what starts a comment far more than in how they
    /// are shaped.</summary>
    internal enum Syntax
    {
        CLike,
        Markup,
        Css,
        Yaml
    }

    /// <summary>What the C-like tokenizer needs to know about one language.</summary>
    /// <param name="StringDelimiters">Every character that opens a string. A
    /// backtick is one in JavaScript and in shell, and neither is a comment.</param>
    /// <param name="PascalCaseTypes">Whether an unknown identifier starting with
    /// a capital reads as a type. True where the convention is enforced by
    /// culture (C#, TypeScript), false where it is not (SQL, shell).</param>
    /// <param name="DollarVariables">Whether <c>$name</c> is one token — a
    /// variable — rather than an operator followed by a word.</param>
    /// <param name="PropertyStrings">Whether a string followed by a colon is a
    /// key rather than a value. JSON's keys are strings, and colouring them the
    /// same as their values is what makes an unindented object unreadable.</param>
    internal sealed record Grammar(
        Syntax Syntax,
        IReadOnlySet<string> Keywords,
        IReadOnlySet<string> Types,
        string? LineComment = null,
        string? BlockCommentStart = null,
        string? BlockCommentEnd = null,
        string StringDelimiters = "\"'",
        bool PascalCaseTypes = false,
        bool DollarVariables = false,
        bool PropertyStrings = false);

    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Every alias that names a language, mapped to its canonical id.
    /// A fence is as likely to say <c>cs</c> or <c>c#</c> as <c>csharp</c>.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = "csharp", ["cs"] = "csharp", ["c#"] = "csharp", ["dotnet"] = "csharp",
        ["javascript"] = "javascript", ["js"] = "javascript", ["jsx"] = "javascript", ["mjs"] = "javascript", ["node"] = "javascript",
        ["typescript"] = "typescript", ["ts"] = "typescript", ["tsx"] = "typescript",
        ["json"] = "json", ["jsonc"] = "json",
        ["yaml"] = "yaml", ["yml"] = "yaml",
        ["css"] = "css",
        ["html"] = "html", ["htm"] = "html",
        ["xml"] = "xml", ["xaml"] = "xml", ["csproj"] = "xml", ["svg"] = "xml",
        ["sql"] = "sql",
        ["bash"] = "bash", ["sh"] = "bash", ["shell"] = "bash", ["zsh"] = "bash", ["console"] = "bash",
        ["powershell"] = "powershell", ["ps1"] = "powershell", ["pwsh"] = "powershell",
        ["mermaid"] = "mermaid", ["mmd"] = "mermaid"
    };

    private static readonly Dictionary<string, Grammar> Grammars = new(StringComparer.Ordinal)
    {
        ["csharp"] = new(
            Syntax.CLike,
            Words("""
                abstract as async await base bool break byte case catch char checked class const continue decimal
                default delegate do double dynamic else enum event explicit extern false file finally fixed float
                for foreach get global goto if implicit in init int interface internal is lock long nameof namespace
                new nint nuint null object operator out override params partial private protected public readonly
                record ref required return sbyte sealed set short sizeof stackalloc static string struct switch this
                throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where
                while with yield
                """),
            Words("Task ValueTask IEnumerable IReadOnlyList List Dictionary HashSet Span ReadOnlySpan"),
            LineComment: "//",
            BlockCommentStart: "/*",
            BlockCommentEnd: "*/",
            PascalCaseTypes: true),

        ["javascript"] = new(
            Syntax.CLike,
            Words("""
                async await break case catch class const constructor continue debugger default delete do else export
                extends false finally for from function get if import in instanceof let new null of return set static
                super switch this throw true try typeof undefined var void while with yield
                """),
            Words("Array Boolean Date Error JSON Map Math Number Object Promise RegExp Set String Symbol"),
            LineComment: "//",
            BlockCommentStart: "/*",
            BlockCommentEnd: "*/",
            StringDelimiters: "\"'`",
            PascalCaseTypes: true),

        ["typescript"] = new(
            Syntax.CLike,
            Words("""
                abstract as asserts async await break case catch class const constructor continue declare default
                delete do else enum export extends false finally for from function get if implements import in infer
                instanceof interface is keyof let namespace new null of private protected public readonly return
                satisfies set static super switch this throw true try type typeof undefined var void while yield
                """),
            Words("any bigint boolean never number object Promise Readonly Record string symbol unknown void Array Map Set"),
            LineComment: "//",
            BlockCommentStart: "/*",
            BlockCommentEnd: "*/",
            StringDelimiters: "\"'`",
            PascalCaseTypes: true),

        // JSON has no keywords beyond its three literals, and its keys are
        // strings — PropertyStrings is what tells them apart from values.
        ["json"] = new(
            Syntax.CLike,
            Words("true false null"),
            None,
            PropertyStrings: true),

        ["sql"] = new(
            Syntax.CLike,
            Words("""
                add all alter and any as asc begin between by case cast check column commit constraint create
                cross cursor database default delete desc distinct drop else end exists foreign from full group
                having identity if in index inner insert intersect into is join key left like limit not null offset
                on or order outer primary references replace returning right rollback select set table then top
                transaction truncate union unique update values view when where with
                """, ignoreCase: true),
            Words("bigint bit boolean char date datetime decimal float int nvarchar text time timestamp uuid varchar", ignoreCase: true),
            LineComment: "--",
            BlockCommentStart: "/*",
            BlockCommentEnd: "*/"),

        ["bash"] = new(
            Syntax.CLike,
            Words("""
                case cd do done echo elif else esac exit export fi for function if in local read return set shift
                source then trap unset until while
                """),
            None,
            LineComment: "#",
            StringDelimiters: "\"'`",
            DollarVariables: true),

        ["powershell"] = new(
            Syntax.CLike,
            Words("""
                begin break catch class continue do dynamicparam else elseif end enum exit filter finally for
                foreach function if in param process return switch throw trap try until using while
                """, ignoreCase: true),
            None,
            LineComment: "#",
            BlockCommentStart: "<#",
            BlockCommentEnd: "#>",
            StringDelimiters: "\"'",
            DollarVariables: true),

        // Mermaid is here for the source, not for the picture: DiagramView draws
        // a mermaid fence inside an entry, and this is what colours the same
        // text when it is being written or reviewed rather than rendered.
        //
        // Case-sensitive, unlike SQL: mermaid's own keywords are written one way
        // (`end`, `subgraph`, `sequenceDiagram`) and node ids are written by the
        // author, so matching loosely would paint a node called `End` or `Class`
        // as syntax.
        ["mermaid"] = new(
            Syntax.CLike,
            Words("""
                accTitle accDescr activate alt and autonumber block class classDef classDiagram click deactivate
                direction else end erDiagram flowchart gantt gitGraph graph journey link loop mindmap note opt par
                participant pie quadrantChart rect requirementDiagram section sequenceDiagram state stateDiagram
                style subgraph timeline title
                """),
            // The direction a diagram runs in, which is the one place mermaid
            // gives a bare word a meaning of its own.
            Words("TB TD BT RL LR"),
            LineComment: "%%"),

        ["css"] = new(Syntax.Css, None, None),
        ["html"] = new(Syntax.Markup, None, None),
        ["xml"] = new(Syntax.Markup, None, None),
        ["yaml"] = new(Syntax.Yaml, None, None)
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["csharp"] = "C#",
        ["javascript"] = "JavaScript",
        ["typescript"] = "TypeScript",
        ["json"] = "JSON",
        ["yaml"] = "YAML",
        ["css"] = "CSS",
        ["html"] = "HTML",
        ["xml"] = "XML",
        ["sql"] = "SQL",
        ["bash"] = "Shell",
        ["powershell"] = "PowerShell",
        ["mermaid"] = "Mermaid"
    };

    /// <summary>Every language that gets colour, in the order the storybook
    /// shows them: the ones the product itself is written in first.</summary>
    public static IReadOnlyList<CodeLanguage> All { get; } =
    [
        .. new[] { "csharp", "typescript", "javascript", "json", "yaml", "css", "html", "xml", "sql", "bash", "powershell", "mermaid" }
            .Select(id => new CodeLanguage(id, Labels[id]))
    ];

    /// <summary>The canonical id behind whatever a fence called it, or null when
    /// nothing here understands it.</summary>
    public static string? Resolve(string? language)
    {
        var name = language?.Trim();
        if (string.IsNullOrEmpty(name)) return null;

        return Aliases.TryGetValue(name, out var id) ? id : null;
    }

    public static bool IsSupported(string? language) => Resolve(language) is not null;

    /// <summary>
    /// The language a file is written in, read from its name. An extension is
    /// just another alias — <c>.cs</c> and a <c>cs</c> fence name the same
    /// grammar — so this asks the same table <see cref="Resolve"/> does rather
    /// than keeping a second list that could disagree with it.
    /// <para>
    /// Null means "nothing here claims to know", which is the caller's cue to
    /// show the file as prose instead of as code. <c>.md</c> and <c>.txt</c>
    /// land there on purpose: they are not missing from the list, they are
    /// files that are already readable without colour.
    /// </para>
    /// </summary>
    public static string? ForFileName(string? fileName)
    {
        var name = fileName?.Trim();
        if (string.IsNullOrEmpty(name)) return null;

        // The last segment only: a folder called `src.cs` above a file called
        // `notes` must not make the file C#.
        var separator = name.LastIndexOfAny(['/', '\\']);
        if (separator >= 0) name = name[(separator + 1)..];

        var dot = name.LastIndexOf('.');

        // No extension, a name that is nothing but a dot part (`.gitignore`), or
        // a trailing dot with nothing after it. None of those name a language.
        if (dot <= 0 || dot == name.Length - 1) return null;

        return Resolve(name[(dot + 1)..]);
    }

    /// <summary>What to print on the badge. An unrecognised language keeps the
    /// name it was given — the block is still that language, the highlighter
    /// just has nothing to say about it.</summary>
    public static string Label(string? language) =>
        Resolve(language) is { } id ? Labels[id] : language?.Trim() ?? string.Empty;

    internal static Grammar? GrammarFor(string? language) =>
        Resolve(language) is { } id ? Grammars[id] : null;

    /// <summary>A keyword set. <paramref name="ignoreCase"/> is for the
    /// languages where casing is the author's taste rather than the language's
    /// rule: <c>SELECT</c> and <c>select</c> are the same word, and only one of
    /// them being coloured is the kind of detail that makes a highlighter look
    /// broken.</summary>
    private static IReadOnlySet<string> Words(string words, bool ignoreCase = false) =>
        new HashSet<string>(
            words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
}
