using System.Text;

namespace Backlog.SharedKernel.Ai;

/// <summary>
/// The one way every <see cref="IAiContentSource"/> fits its records into the
/// budget, so the sources cannot drift on it.
/// </summary>
/// <remarks>
/// <para>
/// Two steps behind one call. <see cref="Compose{T}"/> is what a source calls;
/// under it <see cref="Select{T}"/> decides which records go — the ones the
/// reader has open first, then the ones that share the most words with the
/// question, until the budget is spent — and <see cref="Render"/> writes them
/// out under one line that says whether anything was left out. A source that
/// did its own choosing would be a source whose idea of relevance nobody else
/// could read, and a source that did its own rendering would be a second header
/// format for the assistant to learn.
/// </para>
/// <para>
/// Relevance is deliberately naive — shared words, nothing more. It is not
/// trying to answer the question; it is trying to make sure that when six
/// thousand characters of a sixty-thousand-character backlog go over the wire,
/// the entry the question is about is among them. Word overlap is enough for
/// that, needs no index, and is the same on every machine.
/// </para>
/// </remarks>
public static class AiContentBudget
{
    /// <summary>
    /// How much content a question carries by default, in characters.
    /// <para>
    /// Roughly fifteen hundred tokens. The deployment behind the panel is
    /// provisioned in thousands of tokens per minute, and the old scrape sent
    /// twelve thousand characters of task rows on every press — one question was
    /// a minute's quota. This leaves room for the question, the answer and a
    /// second question in the same minute.
    /// </para>
    /// </summary>
    public const int DefaultCharacters = 6000;

    /// <summary>What sits between two records in the body. Counted against the
    /// budget with the record it follows, so the body's length is what it
    /// says it is.</summary>
    public const string Separator = "\n---\n";

    /// <summary>
    /// Words that appear in almost every question and tell nothing about which
    /// record it is about. Small on purpose: a long list starts removing words
    /// that are the subject of some question, and a short one only costs a few
    /// meaningless ties.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "any", "can", "had", "has", "have",
        "her", "was", "one", "our", "out", "she", "his", "him", "its", "they", "them", "this", "that",
        "these", "those", "with", "what", "which", "who", "whom", "how", "why", "when", "where",
        "does", "did", "will", "would", "should", "could", "about", "into", "from", "than", "then",
        "there", "here", "some", "such", "only", "also", "just", "very", "more", "most", "much",
        "next", "now", "please", "tell", "show", "give", "want", "need", "like", "let", "get"
    };

    /// <summary>
    /// The words of the question worth matching on: lower case, three letters or
    /// longer, and not a stop word. Public because a source with its own search
    /// index (Devbook) wants to ask that index the same words this ranks by.
    /// </summary>
    public static IReadOnlyList<string> Terms(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return [];

        var terms = new List<string>();
        var word = new StringBuilder();

        // One pass, and a word closes on the first character that is not part of
        // one or on the end of the input, so the last word is handled by the same
        // code as the rest.
        for (var index = 0; index <= question.Length; index++)
        {
            var isWordCharacter = index < question.Length && char.IsLetterOrDigit(question[index]);

            if (isWordCharacter)
            {
                word.Append(char.ToLowerInvariant(question[index]));
                continue;
            }

            if (word.Length >= 3)
            {
                var term = word.ToString();
                if (!StopWords.Contains(term) && !terms.Contains(term, StringComparer.Ordinal)) terms.Add(term);
            }

            word.Clear();
        }

        return terms;
    }

    /// <summary>
    /// One area's content, start to finish: the records that fit, under the line
    /// that says what they are. What every source calls, so the sources differ
    /// only in what a record is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The header is charged against the budget before any record is, so a body
    /// is never longer than the budget it was given. It is reserved at its longest
    /// form — the trimmed one, with the total written twice — because which form
    /// renders is not known until the records are chosen, and a reservation that
    /// came up short would be a body that came up long.
    /// </para>
    /// </remarks>
    /// <param name="areaKey">The source's key, repeated on the answer.</param>
    /// <param name="title">What the area is called on the first line.</param>
    /// <param name="records">The area's records, in the order they mean something.</param>
    /// <param name="text">The record as it will appear in the body.</param>
    /// <param name="question">What the reader typed.</param>
    /// <param name="budgetCharacters">How long the body may be, header included.</param>
    /// <param name="pinned">Which records go first whatever the question says.</param>
    /// <param name="total">How many records the area really has, when the list
    /// handed in is already a cap on it — a catalog that stops at two hundred
    /// sessions of eight hundred. Null means the list is the whole area.</param>
    /// <param name="note">One line under the header about the records as a
    /// whole — a repository that could not be read — or null for none. Charged
    /// against the budget like everything else.</param>
    public static AiContent Compose<T>(
        string areaKey,
        string title,
        IReadOnlyList<T> records,
        Func<T, string> text,
        string? question,
        int budgetCharacters,
        Func<T, bool>? pinned = null,
        int? total = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        var known = Math.Max(total ?? records.Count, records.Count);
        var reserved = HeaderReserve(title, known) + (note is null ? 0 : note.Length + 1);

        var selection = Select(records, text, question, budgetCharacters - reserved, pinned);
        var shown = selection.Selected.Count;
        var trimmed = shown < known;

        var body = Render(title, selection.Selected.Select(text), shown, known, trimmed);
        if (note is not null) body = body.Insert(FirstLineLength(body), "\n" + note);

        return new AiContent(areaKey, body, shown, known, trimmed);
    }

    /// <summary>How much of a budget the first line can take, at its longest:
    /// the trimmed form with <paramref name="total"/> in both places, plus the
    /// line break after it. Public so a source that fits its records before
    /// handing them over — Devbook cuts a long chapter — can leave this much room.</summary>
    public static int HeaderReserve(string title, int total) =>
        Header(title, total, total, trimmed: true).Length + 1;

    /// <summary>
    /// Chooses the records that go, in the order they came.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rank: pinned records first, then by how many distinct question terms the
    /// record contains, then by the order they came in — so two records the
    /// question says nothing about keep whatever order the source gave them,
    /// which is the order the reader sees them in. Then records are taken in rank
    /// order while the running length fits the budget: each record's text, plus a
    /// separator for every record after the first. A record that does not fit is
    /// skipped and the walk goes on, because a later, shorter record may — and the
    /// answer is a set, so the pinned record still goes first however long it is,
    /// provided it fits at all.
    /// </para>
    /// <para>
    /// The result is put back into the original order. Rank is for choosing; it
    /// is not a fact about the content, and a body that reordered a plan by word
    /// overlap would be telling the assistant something untrue about the plan.
    /// </para>
    /// </remarks>
    /// <param name="records">The area's records, in the order they mean something.</param>
    /// <param name="text">The record as it will appear in the body; what the
    /// budget is charged for and what the question is matched against.</param>
    /// <param name="question">What the reader typed.</param>
    /// <param name="budgetCharacters">How long the joined records may be. The
    /// header is not this method's business — <see cref="Compose{T}"/> takes it
    /// off the top before calling here.</param>
    /// <param name="pinned">Which records go first whatever the question says —
    /// the one the reader has open, typically. Null pins nothing.</param>
    public static AiContentSelection<T> Select<T>(
        IReadOnlyList<T> records,
        Func<T, string> text,
        string? question,
        int budgetCharacters,
        Func<T, bool>? pinned = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(text);

        var terms = Terms(question);

        // The texts once, because rank reads them and the budget charges for them.
        var texts = new string[records.Count];
        for (var index = 0; index < records.Count; index++) texts[index] = text(records[index]) ?? string.Empty;

        var ranked = Enumerable.Range(0, records.Count)
            .OrderByDescending(index => pinned is not null && pinned(records[index]))
            .ThenByDescending(index => Score(texts[index], terms))
            .ThenBy(index => index);

        var chosen = new List<int>();
        var used = 0;

        foreach (var index in ranked)
        {
            // The separator is paid by the record it precedes, so the first record
            // pays none and the last leaves none dangling.
            var cost = texts[index].Length + (chosen.Count == 0 ? 0 : Separator.Length);
            if (used + cost > budgetCharacters) continue;

            used += cost;
            chosen.Add(index);
        }

        chosen.Sort();

        return new AiContentSelection<T>(
            [.. chosen.Select(index => records[index])],
            records.Count,
            chosen.Count < records.Count);
    }

    /// <summary>
    /// The body: one line saying what it holds, then the records.
    /// <para>
    /// The first line says the count either way, and says "selected by relevance"
    /// only when something was left out — so the assistant is told, in the
    /// content itself, whether the list in front of it is the whole area or a
    /// slice of it. An assistant that counts the entries and reports the number
    /// as the total is the failure this line exists to prevent.
    /// </para>
    /// </summary>
    public static string Render(string title, IEnumerable<string> records, int shown, int total, bool trimmed)
    {
        ArgumentNullException.ThrowIfNull(records);

        var header = Header(title, shown, total, trimmed);
        var joined = string.Join(Separator, records);

        return joined.Length == 0 ? header : header + "\n" + joined;
    }

    /// <summary>The one date form every source writes: the day, ISO, no locale —
    /// what a record's precision is and what an assistant parses without a second
    /// thought.</summary>
    public static string Date(DateTimeOffset at) => at.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>As <see cref="Date(DateTimeOffset)"/>, for a plan's day-only dates.</summary>
    public static string Date(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Header(string title, int shown, int total, bool trimmed) => trimmed
        ? $"{title}: {shown} of {total} entries, selected by relevance to the question."
        : $"{title}: {total} {(total == 1 ? "entry" : "entries")}.";

    private static int FirstLineLength(string body)
    {
        var lineBreak = body.IndexOf('\n');
        return lineBreak < 0 ? body.Length : lineBreak;
    }

    /// <summary>How many distinct question terms the text contains. Substring
    /// rather than whole-word, so "sync" finds "syncing" and "resync"; a false
    /// match costs a rank, not a fact.</summary>
    private static int Score(string text, IReadOnlyList<string> terms)
    {
        var score = 0;

        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) score++;
        }

        return score;
    }
}

/// <summary>What <see cref="AiContentBudget.Select{T}"/> chose, and out of how many.</summary>
/// <param name="Selected">The records that fit, in the order they came.</param>
/// <param name="Total">How many there were to choose from.</param>
/// <param name="Trimmed">Whether any were left out.</param>
public sealed record AiContentSelection<T>(IReadOnlyList<T> Selected, int Total, bool Trimmed);
