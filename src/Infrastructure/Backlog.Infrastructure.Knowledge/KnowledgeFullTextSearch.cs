using System.Text;

using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// <see cref="IKnowledgeSearch"/> over <c>chapter_fts</c>.
///
/// <para><b>This is the one place local ADR 0004's ladder speaks to a user.</b>
/// Every other consumer degrades quietly to Markdown, because browsing touches
/// the handful of files on screen. Search cannot: reading 639 chapters per
/// keystroke is not a slower answer, it is a hang. So a repository with no
/// generated database gets <see cref="KnowledgeSearchAnswer.Unavailable"/> —
/// carrying a sentence that names both what is missing and the command that
/// writes it — rather than an empty list, which claims something different and
/// untrue. <c>KnowledgeAtlasService</c> already refuses to fall back for the same
/// reason, and its message is the model this one follows.</para>
///
/// <para>Open, ask, dispose, once per query. The generator renames a freshly
/// built file over this one, and on Windows that fails against a handle somebody
/// is holding, so a search box that kept a connection alive between keystrokes
/// would block the writer rather than merely lag it.</para>
/// </summary>
public sealed class KnowledgeFullTextSearch(IKnowledgeFolderSource folders) : IKnowledgeSearch
{
    /// <summary>The word the unavailable message is about, so the sentence reads
    /// as a statement about the reader's search rather than about a file they have
    /// never heard of.</summary>
    private const string Surface = "Search";

    public KnowledgeSearchAnswer Search(string? query, string? repositoryAlias = null, string? scope = null, int limit = 30)
    {
        // Asked before the database is opened. "Nothing typed" is not a rung of
        // the ladder and must not be reported as one - a reader who has typed
        // nothing is not being told their index is missing.
        var expression = KnowledgeSearchExpression.For(query);

        using var database = KnowledgeDatabaseSource.TryOpen(folders, repositoryAlias);

        if (database is null)
        {
            return KnowledgeSearchAnswer.Unavailable(KnowledgeRetrieval.UnavailableMessage(Surface));
        }

        if (expression is null) return KnowledgeSearchAnswer.None;

        var hits = database.SearchChapters(expression, scope, limit)
            .Select(row => new KnowledgeSearchHit(
                new KnowledgeChapterAddress(row.Path, row.Slug, row.Title, row.Folder),
                Excerpt(row.Excerpt),
                row.Score))
            .ToList();

        return KnowledgeSearchAnswer.For(hits);
    }

    /// <summary>
    /// The snippet as one line.
    ///
    /// <para>FTS5 hands back the chapter's own bytes, which are Markdown: line
    /// breaks, table pipes, list bullets and fence markers included. Rendering
    /// them would be a second Markdown surface inside a result row, and leaving
    /// them raw makes a two-line excerpt eight lines tall. Collapsing every run of
    /// whitespace to one space is the smallest change that makes the excerpt
    /// legible while leaving every character the author wrote in it — which
    /// matters, because the excerpt's whole job is to show why this chapter
    /// matched.</para>
    /// </summary>
    internal static string Excerpt(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return string.Empty;

        var text = new StringBuilder(snippet.Length);
        var pendingSpace = false;

        foreach (var character in snippet)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = text.Length > 0;
                continue;
            }

            if (pendingSpace) text.Append(' ');
            pendingSpace = false;
            text.Append(character);
        }

        return text.ToString();
    }
}
