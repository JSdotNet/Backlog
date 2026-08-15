using System.Text;

namespace Backlog.UI.Components.Code;

/// <summary>
/// Splits a snippet into coloured runs. Hand-written and deliberately partial,
/// for the same reason <c>MarkdownPreview</c> is: a code block in a backlog
/// entry is read, not compiled, and a lexer that is right about strings,
/// comments and keywords is right about everything a reader needs. Nothing here
/// parses — an unclosed brace or a half-typed line is not an error, it is just a
/// snippet that ends where it ends.
/// <para>
/// Every token holds verbatim source and the view emits it through the render
/// tree's normal escaping, so a snippet cannot inject markup no matter what it
/// contains.
/// </para>
/// </summary>
public static class CodeHighlighter
{
    /// <summary>Characters that are punctuation in every language here. Quotes
    /// are absent on purpose: a string is found before this runs.</summary>
    private const string OperatorChars = "+-*/%=<>!&|^~?:;,.()[]{}\\@#";

    /// <summary>The whole snippet as one flat run of tokens. Concatenating
    /// <see cref="CodeToken.Text"/> gives back the source unchanged.</summary>
    public static IReadOnlyList<CodeToken> Highlight(string? source, string? language)
    {
        var text = (source ?? string.Empty).Replace("\r\n", "\n");
        if (text.Length == 0) return [];

        var grammar = CodeLanguages.GrammarFor(language);
        if (grammar is null) return [new CodeToken(CodeTokenKind.Plain, text)];

        var scanner = new Scanner(text);

        switch (grammar.Syntax)
        {
            case CodeLanguages.Syntax.Markup:
                TokenizeMarkup(scanner);
                break;
            case CodeLanguages.Syntax.Css:
                TokenizeCss(scanner);
                break;
            case CodeLanguages.Syntax.Yaml:
                TokenizeYaml(scanner);
                break;
            default:
                TokenizeCLike(scanner, grammar);
                break;
        }

        return scanner.Done();
    }

    /// <summary>The same tokens, cut into lines. A token may span lines — a
    /// block comment is one token and five lines — so the cut happens here
    /// rather than in the tokenizers, which never have to think about it.
    /// <para>
    /// A single trailing newline is dropped: a snippet written as a raw string
    /// literal ends with one, and rendering the blank line it implies puts an
    /// unexplained gap under every block.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CodeLine> HighlightLines(string? source, string? language)
    {
        List<List<CodeToken>> lines = [[]];

        foreach (var token in Highlight(source, language))
        {
            var parts = token.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) lines.Add([]);
                if (parts[i].Length > 0) lines[^1].Add(token with { Text = parts[i] });
            }
        }

        if (lines.Count > 1 && lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1);

        return [.. lines.Select((tokens, index) => new CodeLine(index + 1, tokens))];
    }

    // --- C-like: C#, JavaScript, TypeScript, JSON, SQL, shell ---------------

    private static void TokenizeCLike(Scanner scanner, CodeLanguages.Grammar grammar)
    {
        while (scanner.More)
        {
            if (TryComment(scanner, grammar)) continue;
            if (TryString(scanner, grammar)) continue;
            if (TryNumber(scanner)) continue;
            if (TryWord(scanner, grammar)) continue;
            if (TryOperator(scanner, grammar)) continue;

            scanner.Plain(scanner.Current);
            scanner.Index++;
        }
    }

    private static bool TryComment(Scanner scanner, CodeLanguages.Grammar grammar)
    {
        if (grammar.LineComment is { } line && scanner.StartsWith(line))
        {
            var newline = scanner.Text.IndexOf('\n', scanner.Index);
            scanner.Emit(CodeTokenKind.Comment, newline < 0 ? scanner.Length : newline);
            return true;
        }

        if (grammar.BlockCommentStart is { } open
            && grammar.BlockCommentEnd is { } close
            && scanner.StartsWith(open))
        {
            var closed = scanner.Text.IndexOf(close, scanner.Index + open.Length, StringComparison.Ordinal);
            scanner.Emit(CodeTokenKind.Comment, closed < 0 ? scanner.Length : closed + close.Length);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A string, with whatever prefixes the language puts in front of one. C#'s
    /// <c>@"..."</c> escapes a quote by doubling it rather than with a
    /// backslash, which matters: read the other way, <c>@"C:\path\"</c> swallows
    /// the rest of the file.
    /// </summary>
    private static bool TryString(Scanner scanner, CodeLanguages.Grammar grammar)
    {
        var quote = scanner.Index;
        var verbatim = false;

        while (quote < scanner.Length && scanner.Text[quote] is '@' or '$')
        {
            verbatim |= scanner.Text[quote] == '@';
            quote++;
        }

        if (quote >= scanner.Length || !grammar.StringDelimiters.Contains(scanner.Text[quote])) return false;

        var end = ScanString(scanner.Text, quote, scanner.Text[quote], verbatim);

        // In JSON the key is a string too, and a key that is coloured like its
        // value is what makes an object hard to skim.
        var kind = grammar.PropertyStrings && NextNonSpace(scanner.Text, end) == ':'
            ? CodeTokenKind.Attribute
            : CodeTokenKind.String;

        scanner.Emit(kind, end);
        return true;
    }

    /// <summary>Where the string that opens at <paramref name="start"/> ends —
    /// past the closing quote, or at the end of the line for an unterminated
    /// one, so a half-typed snippet does not paint the rest of the block.</summary>
    private static int ScanString(string text, int start, char quote, bool verbatim)
    {
        var index = start + 1;

        while (index < text.Length)
        {
            var current = text[index];

            if (verbatim)
            {
                if (current != quote) { index++; continue; }
                if (index + 1 < text.Length && text[index + 1] == quote) { index += 2; continue; }
                return index + 1;
            }

            if (current == '\\' && index + 1 < text.Length) { index += 2; continue; }
            if (current == quote) return index + 1;
            if (current == '\n') return index;

            index++;
        }

        return index;
    }

    private static bool TryNumber(Scanner scanner)
    {
        var text = scanner.Text;
        var start = scanner.Index;
        var current = text[start];

        if (!char.IsAsciiDigit(current)
            && !(current == '.' && start + 1 < text.Length && char.IsAsciiDigit(text[start + 1]))) return false;

        // A digit inside a name is part of the name: `utf8`, `h1`.
        if (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) return false;

        var end = current == '.' ? start + 1 : start;

        while (end < text.Length)
        {
            // Letters carry hex digits and suffixes (`0xFF`, `1.5f`, `10px`).
            if (char.IsLetterOrDigit(text[end]) || text[end] == '_') { end++; continue; }
            // A dot only continues the number when a digit follows it, so
            // `1.ToString()` is a number and a method call, not one long number.
            if (text[end] == '.' && end + 1 < text.Length && char.IsAsciiDigit(text[end + 1])) { end++; continue; }
            break;
        }

        scanner.Emit(CodeTokenKind.Number, end);
        return true;
    }

    private static bool TryWord(Scanner scanner, CodeLanguages.Grammar grammar)
    {
        var text = scanner.Text;
        var start = scanner.Index;
        var current = text[start];
        var variable = grammar.DollarVariables && current == '$';

        if (!variable && !char.IsLetter(current) && current != '_') return false;

        var end = start + 1;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;

        if (variable)
        {
            // A lone `$` is punctuation: `${name}`, `$(command)`.
            if (end == start + 1) return false;

            scanner.Emit(CodeTokenKind.Type, end);
            return true;
        }

        var word = text[start..end];
        var kind = grammar.Keywords.Contains(word) ? CodeTokenKind.Keyword
            : grammar.Types.Contains(word) ? CodeTokenKind.Type
            : grammar.PascalCaseTypes && char.IsUpper(word[0]) ? CodeTokenKind.Type
            : CodeTokenKind.Plain;

        scanner.Emit(kind, end);
        return true;
    }

    private static bool TryOperator(Scanner scanner, CodeLanguages.Grammar grammar)
    {
        if (!OperatorChars.Contains(scanner.Current)) return false;

        var end = scanner.Index + 1;

        // Punctuation runs together — `);` is one token — but never across the
        // start of a comment, or `x = 1;// note` would lose its comment.
        while (end < scanner.Length && OperatorChars.Contains(scanner.Text[end]) && !StartsCommentAt(scanner, grammar, end))
        {
            end++;
        }

        scanner.Emit(CodeTokenKind.Operator, end);
        return true;
    }

    private static bool StartsCommentAt(Scanner scanner, CodeLanguages.Grammar grammar, int index) =>
        (grammar.LineComment is { } line && scanner.StartsWith(line, index))
        || (grammar.BlockCommentStart is { } open && scanner.StartsWith(open, index));

    // --- Markup: HTML and XML ----------------------------------------------

    private static void TokenizeMarkup(Scanner scanner)
    {
        while (scanner.More)
        {
            if (scanner.StartsWith("<!--"))
            {
                var closed = scanner.Text.IndexOf("-->", scanner.Index + 4, StringComparison.Ordinal);
                scanner.Emit(CodeTokenKind.Comment, closed < 0 ? scanner.Length : closed + 3);
                continue;
            }

            // Only a `<` that could open an element does. `a < b` in text
            // content is a less-than sign, and treating it as a tag would paint
            // the rest of the paragraph.
            if (scanner.Current == '<' && (char.IsLetter(scanner.Peek()) || scanner.Peek() is '/' or '!' or '?'))
            {
                TokenizeTag(scanner);
                continue;
            }

            scanner.Plain(scanner.Current);
            scanner.Index++;
        }
    }

    private static void TokenizeTag(Scanner scanner)
    {
        var text = scanner.Text;

        // The bracket belongs to the element: a reader sees `<section`, not a
        // punctuation mark next to a word.
        var end = scanner.Index + 1;
        if (end < text.Length && text[end] is '/' or '!' or '?') end++;
        while (end < text.Length && IsMarkupNameChar(text[end])) end++;
        scanner.Emit(CodeTokenKind.Tag, end);

        while (scanner.More)
        {
            var current = scanner.Current;

            if (current == '>')
            {
                scanner.Emit(CodeTokenKind.Tag, scanner.Index + 1);
                return;
            }

            if (scanner.Peek() == '>' && current is '/' or '?')
            {
                scanner.Emit(CodeTokenKind.Tag, scanner.Index + 2);
                return;
            }

            if (current is '"' or '\'')
            {
                scanner.Emit(CodeTokenKind.String, ScanString(text, scanner.Index, current, verbatim: false));
                continue;
            }

            if (current == '=')
            {
                scanner.Emit(CodeTokenKind.Operator, scanner.Index + 1);
                continue;
            }

            if (char.IsLetter(current) || current is '_' or ':')
            {
                var name = scanner.Index + 1;
                while (name < text.Length && IsMarkupNameChar(text[name])) name++;
                scanner.Emit(CodeTokenKind.Attribute, name);
                continue;
            }

            scanner.Plain(current);
            scanner.Index++;
        }
    }

    private static bool IsMarkupNameChar(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or ':' or '.';

    // --- CSS ----------------------------------------------------------------

    /// <summary>
    /// CSS has no keywords worth listing — <c>display</c> is not reserved, it is
    /// just a property — so what carries the colour here is position: a name
    /// before a <c>{</c> is a selector, a name before a <c>:</c> inside braces
    /// is a property, and everything after that colon is a value.
    /// </summary>
    private static void TokenizeCss(Scanner scanner)
    {
        var text = scanner.Text;
        var depth = 0;

        while (scanner.More)
        {
            var current = scanner.Current;

            if (scanner.StartsWith("/*"))
            {
                var closed = text.IndexOf("*/", scanner.Index + 2, StringComparison.Ordinal);
                scanner.Emit(CodeTokenKind.Comment, closed < 0 ? scanner.Length : closed + 2);
                continue;
            }

            if (current is '"' or '\'')
            {
                scanner.Emit(CodeTokenKind.String, ScanString(text, scanner.Index, current, verbatim: false));
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                scanner.Plain(current);
                scanner.Index++;
                continue;
            }

            if (current is '{' or '}')
            {
                depth = current == '{' ? depth + 1 : Math.Max(0, depth - 1);
                scanner.Emit(CodeTokenKind.Operator, scanner.Index + 1);
                continue;
            }

            if (depth == 0)
            {
                TokenizeCssSelector(scanner);
                continue;
            }

            if (char.IsLetter(current) || current is '-' or '_')
            {
                var end = scanner.Index;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '-' or '_')) end++;

                var property = NextNonSpace(text, end) == ':';
                var custom = text.AsSpan(scanner.Index, end - scanner.Index).StartsWith("--");

                scanner.Emit(
                    property ? CodeTokenKind.Keyword : custom ? CodeTokenKind.Type : CodeTokenKind.Plain,
                    end);
                continue;
            }

            // `#0F172A` is a colour, which reads as a value rather than as
            // punctuation followed by a broken number.
            if (current == '#' && IsHexDigit(scanner.Peek()))
            {
                var end = scanner.Index + 1;
                while (end < text.Length && IsHexDigit(text[end])) end++;
                scanner.Emit(CodeTokenKind.Number, end);
                continue;
            }

            if (TryNumber(scanner)) continue;

            scanner.Emit(CodeTokenKind.Operator, scanner.Index + 1);
        }
    }

    /// <summary>The selector, or the at-rule that stands where one would. Read
    /// as one run up to the brace: <c>.sb-story:hover &gt; .sb-story__name</c>
    /// is one thing being named, not eight tokens.</summary>
    private static void TokenizeCssSelector(Scanner scanner)
    {
        var text = scanner.Text;

        if (scanner.Current == '@')
        {
            var word = scanner.Index + 1;
            while (word < text.Length && (char.IsLetterOrDigit(text[word]) || text[word] == '-')) word++;
            scanner.Emit(CodeTokenKind.Keyword, word);
            return;
        }

        var end = scanner.Index;
        while (end < text.Length && text[end] is not ('{' or '}' or ';' or '"' or '\'') && !StartsAt(text, "/*", end)) end++;
        while (end > scanner.Index && char.IsWhiteSpace(text[end - 1])) end--;

        if (end == scanner.Index)
        {
            scanner.Emit(CodeTokenKind.Operator, scanner.Index + 1);
            return;
        }

        scanner.Emit(CodeTokenKind.Type, end);
    }

    private static bool IsHexDigit(char value) => Uri.IsHexDigit(value);

    // --- YAML ---------------------------------------------------------------

    /// <summary>
    /// Line-oriented, because YAML is: what a run of characters means depends on
    /// whether the key on its line has been read yet. Front matter is the reason
    /// this language is here at all — every entry in the product starts with
    /// some.
    /// </summary>
    private static void TokenizeYaml(Scanner scanner)
    {
        var text = scanner.Text;

        while (scanner.More)
        {
            var current = scanner.Current;

            if (current is ' ' or '\t' or '\n')
            {
                scanner.Plain(current);
                scanner.Index++;
                continue;
            }

            if (current == '#')
            {
                var newline = text.IndexOf('\n', scanner.Index);
                scanner.Emit(CodeTokenKind.Comment, newline < 0 ? scanner.Length : newline);
                continue;
            }

            if (scanner.StartsWith("---") || scanner.StartsWith("..."))
            {
                scanner.Emit(CodeTokenKind.Operator, scanner.Index + 3);
                continue;
            }

            if (current == '-' && scanner.Peek() is ' ' or '\n' or '\0')
            {
                scanner.Emit(CodeTokenKind.Operator, scanner.Index + 1);
                continue;
            }

            if (current == ':')
            {
                scanner.Emit(CodeTokenKind.Operator, scanner.Index + 1);
                continue;
            }

            var lineEnd = text.IndexOf('\n', scanner.Index);
            if (lineEnd < 0) lineEnd = text.Length;

            if (current is '"' or '\'')
            {
                var quoted = ScanString(text, scanner.Index, current, verbatim: false);
                scanner.Emit(
                    NextNonSpace(text, quoted) == ':' ? CodeTokenKind.Attribute : CodeTokenKind.String,
                    quoted);
                continue;
            }

            if (KeyEnd(text, scanner.Index, lineEnd) is { } key)
            {
                scanner.Emit(CodeTokenKind.Attribute, key);
                continue;
            }

            TokenizeYamlScalar(scanner, lineEnd);
        }
    }

    /// <summary>Where the key ends, when this run is one. The colon has to be
    /// followed by a space or the line end: <c>http://example.com</c> is a value
    /// with two colons in it and no key at all.</summary>
    private static int? KeyEnd(string text, int start, int lineEnd)
    {
        for (var index = start; index < lineEnd; index++)
        {
            if (text[index] == '#') return null;
            if (text[index] != ':') continue;
            if (index + 1 < lineEnd && text[index + 1] is not ' ' and not '\t') continue;

            var end = index;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;

            return end > start ? end : null;
        }

        return null;
    }

    private static void TokenizeYamlScalar(Scanner scanner, int lineEnd)
    {
        var text = scanner.Text;
        var end = scanner.Index;

        // ` #` starts a comment; a `#` with no space in front of it is part of
        // the value, which is what a colour like `#F2C14E` in front matter is.
        while (end < lineEnd && !(text[end] == '#' && end > scanner.Index && char.IsWhiteSpace(text[end - 1]))) end++;
        while (end > scanner.Index && char.IsWhiteSpace(text[end - 1])) end--;

        if (end == scanner.Index)
        {
            scanner.Plain(scanner.Current);
            scanner.Index++;
            return;
        }

        var scalar = text[scanner.Index..end];
        var kind = scalar is "true" or "false" or "null" or "yes" or "no" or "~" ? CodeTokenKind.Keyword
            : double.TryParse(scalar, System.Globalization.CultureInfo.InvariantCulture, out _) ? CodeTokenKind.Number
            : CodeTokenKind.Plain;

        scanner.Emit(kind, end);
    }

    // --- Shared -------------------------------------------------------------

    private static char NextNonSpace(string text, int index)
    {
        while (index < text.Length && text[index] is ' ' or '\t') index++;

        return index < text.Length ? text[index] : '\0';
    }

    private static bool StartsAt(string text, string value, int index) =>
        index + value.Length <= text.Length && text.AsSpan(index, value.Length).SequenceEqual(value);

    /// <summary>
    /// A cursor over the source with somewhere to put the characters nobody
    /// claimed. Plain text is buffered rather than emitted per character, so a
    /// line of prose in a markup file is one token instead of forty.
    /// </summary>
    private sealed class Scanner(string text)
    {
        private readonly List<CodeToken> _tokens = [];
        private readonly StringBuilder _plain = new();

        public string Text { get; } = text;

        public int Index { get; set; }

        public int Length => Text.Length;

        public bool More => Index < Text.Length;

        public char Current => Text[Index];

        /// <summary>The character <paramref name="offset"/> ahead, or NUL past
        /// the end — so a lookahead never has to bounds-check first.</summary>
        public char Peek(int offset = 1) => Index + offset < Text.Length ? Text[Index + offset] : '\0';

        public bool StartsWith(string value) => StartsAt(Text, value, Index);

        public bool StartsWith(string value, int index) => StartsAt(Text, value, index);

        public void Plain(char value) => _plain.Append(value);

        /// <summary>Takes everything up to <paramref name="end"/> as one token
        /// and moves past it. A <see cref="CodeTokenKind.Plain"/> token joins the
        /// buffer instead, so neighbouring plain runs stay one token.</summary>
        public void Emit(CodeTokenKind kind, int end)
        {
            if (end <= Index) return;

            if (kind == CodeTokenKind.Plain)
            {
                _plain.Append(Text, Index, end - Index);
                Index = end;
                return;
            }

            Flush();
            _tokens.Add(new CodeToken(kind, Text[Index..end]));
            Index = end;
        }

        public IReadOnlyList<CodeToken> Done()
        {
            Flush();
            return _tokens;
        }

        private void Flush()
        {
            if (_plain.Length == 0) return;

            _tokens.Add(new CodeToken(CodeTokenKind.Plain, _plain.ToString()));
            _plain.Clear();
        }
    }
}
