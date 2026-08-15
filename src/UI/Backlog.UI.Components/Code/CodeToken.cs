namespace Backlog.UI.Components.Code;

/// <summary>
/// What a run of characters turned out to be. These are the distinctions a
/// reader makes at a glance — is this prose or is it code, is it live or is it
/// commented out — and not the categories a compiler would draw: there is no
/// separate kind for a method name or a generic parameter, because colouring
/// those apart buys nothing you could not read from the shape of the line.
/// </summary>
public enum CodeTokenKind
{
    /// <summary>Anything the highlighter has no opinion about, including all
    /// whitespace. Renders as bare text, so an unknown language costs nothing
    /// but a plain, readable block.</summary>
    Plain,
    Keyword,
    Type,
    String,
    Number,
    Comment,
    Operator,

    /// <summary>A markup element, brackets included: <c>&lt;section</c>.</summary>
    Tag,

    /// <summary>An attribute name in markup, or the key half of a JSON or YAML
    /// pair — the same job in three notations, so the same colour.</summary>
    Attribute
}

/// <summary>One coloured run. <see cref="Text"/> is verbatim source: the
/// highlighter never rewrites, trims or normalises what it was given, so the
/// concatenation of every token is exactly the source it was handed.</summary>
public sealed record CodeToken(CodeTokenKind Kind, string Text)
{
    /// <summary>The class the view puts on this run. Lives here rather than in
    /// the view so the kinds and the stylesheet's <c>code-token--*</c> rules
    /// stay a matched pair.</summary>
    public string CssClass => Kind switch
    {
        CodeTokenKind.Keyword => "code-token code-token--keyword",
        CodeTokenKind.Type => "code-token code-token--type",
        CodeTokenKind.String => "code-token code-token--string",
        CodeTokenKind.Number => "code-token code-token--number",
        CodeTokenKind.Comment => "code-token code-token--comment",
        CodeTokenKind.Operator => "code-token code-token--operator",
        CodeTokenKind.Tag => "code-token code-token--tag",
        CodeTokenKind.Attribute => "code-token code-token--attribute",
        _ => "code-token"
    };
}

/// <summary>One source line. The line is the unit because the gutter numbers
/// them and a token may span several of them — a block comment is one token and
/// five lines.</summary>
public sealed record CodeLine(int Number, IReadOnlyList<CodeToken> Tokens);
