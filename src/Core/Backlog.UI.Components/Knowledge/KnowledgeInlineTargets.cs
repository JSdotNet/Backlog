using Backlog.UI.Components.Markdown;

using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// What a reference written in the prose of a knowledge document renders as, as one
/// answer the whole product shares.
///
/// <para>It lives here rather than inside <c>MarkdownView</c> because that view is
/// not the only thing rendering a knowledge chapter. The arc42 fallback and the
/// design sections render their own block trees straight through
/// <see cref="MarkdownRender"/>, and while they passed no hook at all their links
/// were browser anchors on repository paths — the same bug, in two places the fix
/// to the view could not reach. A second copy of the rule in each of them would
/// have been a third and fourth spelling of "is this a chapter", so there is one
/// spelling and they are handed it.</para>
///
/// <para>It also keeps the decision out of the application screens. A panel that
/// built its own replacement would be hand-rolling an anchor or a button where the
/// library already draws one — the thing
/// <c>.github/instructions/ui-components.instructions.md</c> exists to stop — so
/// what a panel supplies is the two facts only it has: which document is on screen,
/// and what to do when a reader follows a reference out of it.</para>
/// </summary>
public static class KnowledgeInlineTargets
{
    /// <summary>
    /// The hook for a document at <paramref name="documentPath"/>.
    ///
    /// <para>Always a hook, never null: whether this <em>is</em> a knowledge
    /// document is the caller's question and not this one's, and a caller that has
    /// not decided must not pass the hook on at all. The
    /// <c>MarkdownView</c> that renders both a chapter and an entry's body is the
    /// one place where that decision lives.</para>
    /// </summary>
    /// <param name="documentPath">The repository-relative path of the document being
    /// rendered, carrying its area folder. It is what a relative link resolves
    /// against, so an area-relative spelling would resolve every link in the file
    /// into a folder that does not exist; a host whose own path is area-relative
    /// normalises it before handing it over. Null is a document that cannot say
    /// where it is, which leaves the relative links in it unfollowable rather than
    /// guessed at.</param>
    /// <param name="hrefFor">The host's route for a reference, when it has one.</param>
    /// <param name="onNavigate">Raised with the reference a reader followed.</param>
    public static MarkdownRender.MarkdownInlineTarget For(
        string? documentPath,
        Func<KnowledgeReference, string?>? hrefFor,
        EventCallback<KnowledgeReference> onNavigate) =>
        (target, text, kind) => Draw(target, text, kind, documentPath, hrefFor, onNavigate);

    /// <summary>
    /// The rule itself.
    ///
    /// <para>A code span is read exactly as it was before any of this: rooted form
    /// only, because a span is a command, a type name or a single word far more
    /// often than it is a path, and <c>domain.md</c> resolved against the chapter
    /// around it would put a destination on the most quotable file name in the
    /// repository.</para>
    ///
    /// <para>A link is read against the document, because the author wrote the
    /// syntax of a thing to be followed. What it cannot place divides in two. A
    /// target naming a scheme was never this to place — an <c>https</c> link is
    /// somebody else's destination and stays the anchor
    /// <see cref="MarkdownRender"/> already writes — so it is handed back. A
    /// relative one that reaches no chapter is the author's own mistake, or an
    /// image, or a path that climbs out of the repository, and none of those is
    /// worth opening a browser for: the words stay, the destination does not. That
    /// is the same bargain <see cref="MarkdownRender"/> already strikes for a
    /// scheme it will not navigate to, wearing the same class, so a reader meets one
    /// shape of unfollowable link rather than two.</para>
    ///
    /// <para>An image splits the same two ways and settles both without asking
    /// where the file is. A scheme is somebody else's picture, hosted where it is
    /// reachable, and stays the <c>img</c> <see cref="MarkdownRender"/> already
    /// writes. A relative target is the file beside the chapter, and there is no
    /// origin it could be resolved against: the folders sit outside every
    /// <c>wwwroot</c>, so the <c>src</c> the author wrote is a request to whatever
    /// host happens to be serving the app, which answers 404. So it is not
    /// resolved — it is dropped, and the alt text stands in its place. Which is
    /// the answer a link already gave to this exact target: <c>assets/shot.png</c>
    /// written as <c>[…](…)</c> has been inert since relative links were read at
    /// all, and the same path written as <c>![…](…)</c> reaching a different
    /// verdict was the whole of the defect.</para>
    ///
    /// <para>It is a smaller loss than it sounds. The picture is still correct
    /// where the file is read as plain markdown — a checkout, or GitHub — because
    /// there the relative path is the right spelling and this changes nothing
    /// about it. What a reader loses is a broken-image icon.</para>
    ///
    /// <para>Nothing here reaches for the file, and that is the point rather than
    /// an omission. Serving it would make this a file server with a root to pin
    /// and a traversal rule to get right, and inlining it would put a synchronous
    /// disk read inside a render pass; both belong to a decision about whether the
    /// knowledge pane displays pictures at all, which is not a decision a
    /// rendering rule gets to make on its own.</para>
    /// </summary>
    private static RenderFragment? Draw(
        string target,
        string text,
        MarkdownInlineKind kind,
        string? documentPath,
        Func<KnowledgeReference, string?>? hrefFor,
        EventCallback<KnowledgeReference> onNavigate)
    {
        // Asked and answered before a reference is looked for at all, because an
        // image never becomes one. `![the model](domain.md)` is an author writing
        // one syntax and meaning the other, and turning it into the control a link
        // would have become would be this deciding what they meant.
        if (kind is MarkdownInlineKind.Image)
        {
            return MarkdownRender.NamesScheme(target) ? null : Inert(text, "md-image--inert");
        }

        var reference = kind is MarkdownInlineKind.Link
            ? KnowledgeReference.ParseKnowledgePath(target, documentPath)
            : KnowledgeReference.ParseKnowledgePath(target);

        if (reference is not null)
        {
            // The author's own wording wherever they wrote any: a link brings words
            // chosen to read in the sentence, and printing the path over them would
            // lose what they were saying. A code span is its own path, and there is
            // nothing else it could say.
            var label = string.Equals(target, text, StringComparison.Ordinal) ? null : text;
            return Link(reference, label, hrefFor?.Invoke(reference), onNavigate);
        }

        if (kind is not MarkdownInlineKind.Link || MarkdownRender.NamesScheme(target)) return null;

        return Inert(text, "md-link--inert");
    }

    private static RenderFragment Link(
        KnowledgeReference reference,
        string? text,
        string? href,
        EventCallback<KnowledgeReference> onNavigate) => builder =>
    {
        builder.OpenComponent<KnowledgeReferenceLink>(0);
        builder.AddComponentParameter(1, nameof(KnowledgeReferenceLink.Reference), reference);
        builder.AddComponentParameter(2, nameof(KnowledgeReferenceLink.Text), text);
        builder.AddComponentParameter(3, nameof(KnowledgeReferenceLink.Href), href);
        builder.AddComponentParameter(4, nameof(KnowledgeReferenceLink.OnNavigate), onNavigate);
        builder.CloseComponent();
    };

    /// <summary>The words with no destination behind them. The class is the
    /// caller's because <see cref="MarkdownRender"/> already draws both shapes —
    /// <c>md-link--inert</c> for a link it will not follow, <c>md-image--inert</c>
    /// for a picture it will not fetch — and a reader should meet the same two
    /// here rather than a third of this file's own.</summary>
    private static RenderFragment Inert(string text, string className) => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", className);
        builder.AddContent(2, text);
        builder.CloseElement();
    };
}
