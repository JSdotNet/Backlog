using System.Text;

namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>What a token is, as far as the grammar cares.</summary>
public enum C4TokenKind
{
    /// <summary>A bare word: a keyword, an identifier, a number, a colour.</summary>
    Word,

    /// <summary>A double-quoted string, with its quotes removed and its escapes
    /// resolved. Held apart from <see cref="Word"/> because the distinction
    /// decides a view header: <c>systemLandscape ref1 "Title"</c> gives a bare key
    /// and a quoted description, and reading them the same way would swap them.</summary>
    Text,

    /// <summary>The <c>-&gt;</c> of a relationship.</summary>
    Arrow,

    /// <summary>The <c>=</c> of an identifier assignment.</summary>
    Assign,

    BraceOpen,
    BraceClose,

    /// <summary>End of a statement. Structurizr statements are line-delimited, so
    /// this is a newline that had tokens before it.</summary>
    EndOfStatement
}

/// <param name="Line">One-based line in the source, so a problem can name it.</param>
public sealed record C4Token(C4TokenKind Kind, string Text, int Line)
{
    public bool IsWord(string word) =>
        Kind == C4TokenKind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this token can serve as a value: a bare word or a quoted
    /// string. What the argument reader accepts.</summary>
    public bool IsValue => Kind is C4TokenKind.Word or C4TokenKind.Text;
}

/// <summary>
/// Turns Structurizr DSL text into tokens.
/// <para>
/// Line-oriented on purpose. The DSL has no statement terminator: a statement ends
/// where its line ends, unless it opens a brace. So newlines are tokens rather than
/// whitespace, and everything above this can be a plain statement loop instead of
/// a grammar that has to guess where one declaration stops and the next begins.
/// </para>
/// <para>
/// Comments are removed here rather than in the parser, because all three forms
/// (<c>#</c>, <c>//</c>, <c>/* */</c>) may appear mid-line and only outside a
/// quoted string — which is a question about characters, not about grammar. The
/// block form still counts its newlines, so every token afterwards reports the
/// line it is actually on.
/// </para>
/// </summary>
public static class C4DslLexer
{
    public static IReadOnlyList<C4Token> Tokenize(string? source)
    {
        var tokens = new List<C4Token>();
        if (string.IsNullOrWhiteSpace(source)) return tokens;

        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var line = 1;
        var index = 0;
        var statementHasTokens = false;

        void EndStatement()
        {
            if (!statementHasTokens) return;
            tokens.Add(new C4Token(C4TokenKind.EndOfStatement, "\n", line));
            statementHasTokens = false;
        }

        void Add(C4TokenKind kind, string value)
        {
            tokens.Add(new C4Token(kind, value, line));
            statementHasTokens = true;
        }

        while (index < text.Length)
        {
            var current = text[index];

            if (current == '\n')
            {
                EndStatement();
                line++;
                index++;
                continue;
            }

            if (current is ' ' or '\t')
            {
                index++;
                continue;
            }

            // Line comments. `#` is overloaded in this grammar: it opens a comment
            // and it also opens every colour a `styles` block states. So a hash is a
            // comment unless what follows it is exactly a hex colour — otherwise
            // `background #438dd5` loses its value and the style silently becomes a
            // style with no colour in it.
            if (current == '#' && HexColourLength(text, index) is { } colour)
            {
                Add(C4TokenKind.Word, text.Substring(index, colour + 1));
                index += colour + 1;
                continue;
            }

            if (current == '#' || (current == '/' && Peek(text, index + 1) == '/'))
            {
                while (index < text.Length && text[index] != '\n') index++;
                continue;
            }

            if (current == '/' && Peek(text, index + 1) == '*')
            {
                index += 2;
                while (index < text.Length && !(text[index] == '*' && Peek(text, index + 1) == '/'))
                {
                    if (text[index] == '\n') line++;
                    index++;
                }

                index = Math.Min(index + 2, text.Length);
                continue;
            }

            if (current == '"')
            {
                var (value, next) = ReadQuoted(text, index);
                Add(C4TokenKind.Text, value);
                index = next;
                continue;
            }

            if (current == '{')
            {
                // A brace ends the statement that opened it, so the parser reads a
                // header and then a block rather than one run-on statement.
                Add(C4TokenKind.BraceOpen, "{");
                EndStatement();
                index++;
                continue;
            }

            if (current == '}')
            {
                EndStatement();
                Add(C4TokenKind.BraceClose, "}");
                EndStatement();
                index++;
                continue;
            }

            if (current == '-' && Peek(text, index + 1) == '>')
            {
                Add(C4TokenKind.Arrow, "->");
                index += 2;
                continue;
            }

            if (current == '=' && Peek(text, index + 1) != '=')
            {
                Add(C4TokenKind.Assign, "=");
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && !IsBreak(text[index])) index++;
            if (index == start) index++;  // never stall on a character nothing claims
            Add(C4TokenKind.Word, text[start..index]);
        }

        EndStatement();
        return tokens;
    }

    /// <summary>The characters that end a bare word. <c>=</c> is deliberately not
    /// one of them beyond the single-character case: <c>element.tag==Database</c> is
    /// one word, and splitting it would turn a filter expression into three tokens
    /// that read like an assignment.</summary>
    private static bool IsBreak(char value) =>
        value is ' ' or '\t' or '\n' or '"' or '{' or '}';

    private static char Peek(string text, int index) =>
        index < text.Length ? text[index] : '\0';

    /// <summary>
    /// The number of hex digits after a <c>#</c> when they spell a colour and stop
    /// cleanly, or null when the hash opens a comment.
    /// <para>
    /// Only the four lengths CSS gives a colour, and only when the run ends at
    /// something that could end a word. <c>#438dd5</c> is a colour; <c>#deadbeef1</c>
    /// and <c>#note 3</c> are comments, and a comment is the safer default because
    /// the alternative — reading prose as a token — turns a note into a syntax
    /// error.
    /// </para>
    /// </summary>
    private static int? HexColourLength(string text, int hash)
    {
        var length = 0;
        while (hash + 1 + length < text.Length && char.IsAsciiHexDigit(text[hash + 1 + length])) length++;

        if (length is not (3 or 4 or 6 or 8)) return null;

        var after = Peek(text, hash + 1 + length);
        return after == '\0' || IsBreak(after) ? length : null;
    }

    private static (string Value, int Next) ReadQuoted(string text, int start)
    {
        var builder = new StringBuilder();
        var index = start + 1;

        while (index < text.Length && text[index] != '"')
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                builder.Append(text[index + 1] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    var escaped => escaped
                });
                index += 2;
                continue;
            }

            // An unterminated string stops at the line end rather than swallowing
            // the rest of the file, so one missing quote costs one statement.
            if (text[index] == '\n') break;

            builder.Append(text[index]);
            index++;
        }

        return (builder.ToString(), index < text.Length && text[index] == '"' ? index + 1 : index);
    }
}
