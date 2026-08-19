using Bunit;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one thing all four knowledge panels owe the reader now that they show a
/// selected chapter through the shared file view: the file is named once.
/// <para>
/// It was reported in exactly that form — two places on one screen showing the
/// same file — and the second place was always the document bar directly under
/// the header that had just said it. So the assertion is not "a file view is
/// present" but "the path the file view's header gives appears nowhere else
/// inside it", which is the shape the bug would have to take to come back.
/// </para>
/// <para>
/// Counted over rendered text and scoped to the file view, on purpose. An id or
/// an aria-label carrying the path is not something a reader sees twice, and the
/// file list beside the document legitimately names every file in it — neither is
/// the duplication, and a count over the whole panel's markup would fail on both.
/// </para>
/// </summary>
internal static class FileViewIdentityAssertions
{
    /// <summary>Asserts that <paramref name="scope"/> holds one file view, that its
    /// header carries <paramref name="expectedName"/> and a path, and that nothing
    /// else in that scope repeats the path.</summary>
    /// <param name="scope">What "on one screen" means for this panel. The document
    /// the file view belongs to, so the panel's own header is inside the count —
    /// that header is where three of these panels used to say the path a second
    /// time.</param>
    internal static void AssertTheFileIsNamedOnce<TComponent>(this IRenderedComponent<TComponent> component, string expectedName, string scope)
        where TComponent : IComponent
    {
        var document = component.Find(scope);

        var view = document.QuerySelector(".file-view");
        Assert.NotNull(view);

        var name = view.QuerySelector(".file-view__name");
        Assert.NotNull(name);
        Assert.Equal(expectedName, name.TextContent.Trim());

        var path = view.QuerySelector(".file-view__path");
        Assert.NotNull(path);

        var identity = path.TextContent.Trim();
        Assert.NotEmpty(identity);
        Assert.Equal(1, Occurrences(document.TextContent, identity));

        // The bar is still there — it carries Copy and Edit — and it is the
        // element that used to say the path a second time. Empty is what "the
        // header above already said it" looks like in the DOM.
        foreach (var title in view.QuerySelectorAll(".markdown-document__title"))
        {
            Assert.Equal(string.Empty, title.TextContent.Trim());
        }
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
