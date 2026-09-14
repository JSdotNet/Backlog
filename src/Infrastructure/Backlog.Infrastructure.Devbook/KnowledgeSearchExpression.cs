using System.Text;

namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// What a reader typed, turned into something FTS5 will accept.
///
/// <para>FTS5's <c>MATCH</c> takes a query language, not a phrase: <c>AND</c>,
/// <c>OR</c>, <c>NOT</c>, <c>NEAR</c>, column filters, parentheses, quoting and
/// prefix stars all mean something in it. Handing a reader's typing to that
/// parser is a syntax error waiting for the first apostrophe — "what's stale?"
/// is an unterminated string, and "C# and F#" is a boolean expression with a
/// column filter in it. So nothing a reader types is ever query syntax here.
/// Every run of letters and digits becomes one quoted term and everything else
/// is dropped.</para>
///
/// <para>The last term also gets a prefix star, because search runs while the
/// reader is still typing: without it, every query is wrong until the moment the
/// final word is finished, and the list flickers empty on the way to being
/// right. Only the last one, because a prefix star on an earlier word widens the
/// query for no reason — those words are complete.</para>
///
/// <para>Nothing here is a stemmer or a spell-checker, deliberately. This corpus
/// is full of exact identifiers — <c>schemaVersion</c>, <c>bm25</c>,
/// <c>_reading-order.json</c> — where a lexical index earns its keep precisely by
/// being literal, and where the tier that forgives a reader's vocabulary is the
/// semantic one beside it.</para>
/// </summary>
internal static class KnowledgeSearchExpression
{
    /// <summary>
    /// How many terms one query may carry.
    /// <para>
    /// A cap rather than no cap because the input is a text box: a paragraph
    /// pasted into it would become a hundred-term conjunction that matches
    /// nothing, slowly. Twelve is past anything anybody types on purpose.
    /// </para>
    /// </summary>
    private const int MaximumTerms = 12;

    /// <summary>
    /// The FTS5 expression for <paramref name="query"/>, or <see langword="null"/>
    /// when there is nothing in it to search for — which is not the same as
    /// nothing matching, and is why the caller stops here rather than running an
    /// empty query.
    /// </summary>
    public static string? For(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var expression = new StringBuilder();
        var terms = 0;
        var start = -1;

        // One pass, and the term is closed either by the first character that is
        // not part of one or by the end of the input - so the trailing term is
        // handled by the same code as the rest rather than by a copy of it after
        // the loop.
        for (var index = 0; index <= query.Length && terms < MaximumTerms; index++)
        {
            var isTermCharacter = index < query.Length && char.IsLetterOrDigit(query[index]);

            if (isTermCharacter)
            {
                if (start < 0) start = index;
                continue;
            }

            if (start < 0) continue;

            if (terms > 0) expression.Append(' ');
            expression.Append('"').Append(query, start, index - start).Append('"');
            terms++;
            start = -1;
        }

        if (terms == 0) return null;

        // The reader may still be typing the last word; everything before it they
        // have finished. A query that ends in a space or a full stop says the word
        // is done, so no star.
        if (char.IsLetterOrDigit(query[^1])) expression.Append('*');

        return expression.ToString();
    }
}
