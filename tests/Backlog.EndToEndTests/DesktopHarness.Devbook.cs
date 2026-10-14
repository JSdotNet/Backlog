using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// A repository's devbook in the Devbook side pane: its width, an Architecture
/// chapter, and the remarks a reader leaves on that chapter's blocks.
///
/// <para>Remarks are the person's own data, kept in the app's database (local ADR
/// 0011) and never in the chapter file, so leaving one changes no file in any
/// repository. The database is the shared <c>Backlog.Debug</c> workspace, so a
/// scenario only ever touches the remarks it created.</para>
/// </summary>
internal sealed partial class DesktopHarness
{
    /// <summary>The side pane's edge, which takes the arrow keys as well as a drag.</summary>
    public ILocator SidePaneResizer => Page.GetByTestId("side-pane-resizer");

    /// <summary>The Architecture chapter the pane shows.</summary>
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

    /// <summary>
    /// Sets the side pane's width with the separator's own keys — Home for its
    /// minimum, then one 2rem step per Arrow Left — the keyboard equivalent of the
    /// drag, held to the same clamp. Returns the width the separator reports,
    /// which is less than asked for where the window cannot give the pane that
    /// much. Below a 72rem window the pane stacks under the Tasks view at the
    /// window's full width and the separator is not drawn; that answers
    /// <c>null</c> and changes nothing.
    /// </summary>
    public async Task<double?> SetSidePaneWidthAsync(double rem)
    {
        if (!await SidePaneResizer.IsVisibleAsync()) return null;

        await SidePaneResizer.FocusAsync();
        await SidePaneResizer.PressAsync("Home");
        await Interactive.EventuallyAsync(
            async () => Rem(await SidePaneResizer.GetAttributeAsync("aria-valuenow")) == 24,
            TimeSpan.FromSeconds(10),
            "the side pane to reach its minimum");

        for (var step = 24.0; step < rem; step += 2)
        {
            var before = Rem(await SidePaneResizer.GetAttributeAsync("aria-valuenow"));
            await SidePaneResizer.PressAsync("ArrowLeft");
            await Interactive.EventuallyAsync(
                async () => Rem(await SidePaneResizer.GetAttributeAsync("aria-valuenow")) != before
                            || before >= Rem(await SidePaneResizer.GetAttributeAsync("aria-valuemax")),
                TimeSpan.FromSeconds(10),
                "the side pane to widen a step");
        }

        return Rem(await SidePaneResizer.GetAttributeAsync("aria-valuenow"));
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

    private static double Rem(string? value) =>
        double.Parse(value ?? "0", CultureInfo.InvariantCulture);
}
