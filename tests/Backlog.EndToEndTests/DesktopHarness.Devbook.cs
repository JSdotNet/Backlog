using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// A repository's devbook in the Devbook, which takes the whole screen: an
/// Architecture chapter, and the remarks a reader leaves on that chapter's blocks.
///
/// <para>Remarks are the person's own data, kept in the app's database (local ADR
/// 0011) and never in the chapter file, so leaving one changes no file in any
/// repository. The database is the shared <c>Backlog.Debug</c> workspace, so a
/// scenario only ever touches the remarks it created.</para>
/// </summary>
internal sealed partial class DesktopHarness
{
    /// <summary>The Architecture chapter the Devbook shows.</summary>
    public ILocator ArchitectureChapter => Page.GetByTestId("arc42-chapter-file");

    /// <summary>Opens one Architecture chapter of the scoped repository from the
    /// pane's menu, by the title the menu lists it under.</summary>
    public async Task OpenArchitectureChapterAsync(Regex title)
    {
        await OpenAreaAsync("Architecture");
        await Interactive.ClickAsync(
            DevbookPane.Locator(".devbook-menu__item--file").Filter(new LocatorFilterOptions { HasTextRegex = title }).First,
            ArchitectureChapter.Filter(new LocatorFilterOptions { HasTextRegex = title }));
    }

    /// <summary>The ids of the remarks drawn on the open chapter.</summary>
    public async Task<IReadOnlySet<string>> RemarkIdsAsync() =>
        (await ArchitectureChapter.Locator("[data-testid^='markdown-comment-body-']")
            .EvaluateAllAsync<string[]>("items => items.map(i => i.dataset.testid.substring('markdown-comment-body-'.length))"))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>One remark, by its id.</summary>
    public ILocator Remark(string id) => ArchitectureChapter.Locator($"[data-testid='markdown-comment-body-{id}']");

    /// <summary>One of a remark's controls — <c>save</c>, <c>cancel</c>,
    /// <c>edit</c>, <c>resolve</c>, <c>ai-resolve</c>, <c>delete</c>.</summary>
    public ILocator RemarkControl(string id, string control) =>
        ArchitectureChapter.Locator($"[data-testid='markdown-comment-{control}-{id}']");

    /// <summary>What is on top at the centre of an element — the element itself
    /// or something inside it, unless another element intercepts the pointer
    /// there. Logged beside a failure, so it names the interceptor.</summary>
    public async Task<string> TopmostAtCentreAsync(ILocator target) =>
        await target.EvaluateAsync<string>(
            """
            el => {
                const box = el.getBoundingClientRect();
                const top = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
                if (!top) return 'nothing (off screen)';
                const owner = top.closest('[data-testid]');
                return (top === el || el.contains(top) ? 'itself' : 'INTERCEPTED by') + ' <' + top.tagName.toLowerCase()
                    + ' class="' + top.className + '"> in [data-testid=' + (owner ? owner.dataset.testid : '-') + ']';
            }
            """);

    /// <summary>The widths that decide the margin layout, for the run log.</summary>
    public Task<string> RemarkLayoutAsync() =>
        ArchitectureChapter.EvaluateAsync<string>(
            """
            view => {
                const row = view.querySelector('.md-view--margin .md-block-row');
                return 'file-view ' + Math.round(view.getBoundingClientRect().width) + 'px'
                    + (row ? ', row ' + getComputedStyle(row).display + ' ' + getComputedStyle(row).gridTemplateColumns : ', no margin row');
            }
            """);
}
