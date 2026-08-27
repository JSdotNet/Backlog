---
applyTo: "src/Harness/Backlog.UI.Storybook/**"
description: How to author a storybook page and a story — which chrome component to use and which parameters to pass, where a new page goes in the index, and what the architecture tests check.
---

# Storybook authoring

`src/Harness/Backlog.UI.Storybook` renders every component of
`src/Core/Backlog.UI.Components` with no application behind it. It is the review
surface for everything `.design/` specifies.

**The rules that govern this host are in
`.design/README.md#living-reference-the-ui-storybook`. Read them before adding a
page or a story.** They cover what a page may show, when a subject earns its own
page, that every sample is its own section, and what folds. This file does not
repeat them — a rule written in two places is a rule that can disagree with
itself, which is the split the storybook exists to hold. What follows is how to
satisfy them in code.

## The three chrome components

Chrome frames an example. It is not a component under review, so it lives here
and never in the library.

| Component | What it is for |
|---|---|
| `Shared/StoryPage.razor` | The page. Title, one-sentence summary, the `.design` chapters that govern it, then the stories. |
| `Shared/Story.razor` | One sample. Renders the `<section>`, the anchor, the test id, and both folds. |
| `Shared/MarkdownStory.razor` | A read view of one markdown sample, so a page does not re-implement parse-and-render per story. |

## Writing a page

```razor
<StoryPage Title="Badges"
           Summary="A value, and the class that says what kind of value it is."
           Guidelines="@Rules">
    <Story ...>
</StoryPage>

@code {
    private static readonly DesignGuideline[] Rules =
    [
        new("color-scheme.md#badge-and-chip-tones", "What each badge family and each value tone means."),
        new("accessibility.md#iconography-accessibility", "Why a glyph never carries the meaning on its own.")
    ];
}
```

`DesignGuideline` takes two strings: the chapter and its anchor relative to
`.design` — no leading folder, `Label` prepends it — and one line saying what
that chapter decides for this page. The second string is the reason to open the
fold, not a summary of what is inside it.

`GuidelineChapter` then reads that section out of the repository's own file and
renders it collapsed, so the rule sits beside the component in the words the
repository holds, with no copy in between. A component page with an empty
`Guidelines` fails review; only the introduction has none.

## Writing a story

```razor
<Story Name="Status badge"
       Description="One or two sentences on how the host uses it."
       Code="@StatusCode">
    <StatusBadge Value="Active" />
</Story>

@code {
    private const string StatusCode = """
        <StatusBadge Value="Active" />
        """;
}
```

| Parameter | Pass it when |
|---|---|
| `Name` | Always. It is the heading, the `#anchor` and the `data-testid` — `Story.Slug` derives all three, so renaming a story moves its permalink. |
| `Description` | Always. Blank lines split paragraphs; `Story` folds it on its own once it passes the threshold, so never pre-fold it by hand. |
| `Code` | The usage that produced the sample. `Language` defaults to `razor`; set it only for C# or CSS. |
| `Source` | The content the sample was handed, when it draws content — the markdown a read view rendered, the fence a diagram came from. `SourceLanguage` defaults to `markdown`, `SourceLabel` to `Source`; name the content, not the act. |
| `SourceContent` | Instead of `Source`, when the content is more than one string — two versions of a file being compared. |

`Code` and `Source` land in one disclosure, two columns when both are present and
one when only one is. Do not add a second `FoldControl` around either.

For a markdown sample, hand the same string to both `MarkdownStory.Source` and
`Story.Source`: the view and the text a reader checks it against then cannot
drift.

## Adding a page

A page is three edits. The first two are tested; the third is not, and getting
it wrong shows up only in the sidebar:

1. The `.razor` file under `Components/Pages/`, with an `@page` route.
2. An entry in `Shared/StorybookIndex.cs`, in the group its subject belongs to.
   The sidebar and the introduction both render from that one list.
3. `Exact: true` on the entry if another page's route sits under its path, or the
   parent stays highlighted while a child is open.

Placement inside `StorybookIndex` follows the ordering rule in `.design`. Where a
page cannot honour it, record the bend in a comment on the entry saying which way
the rule was bent and why — the file already carries several, and they only read
as exceptions because the rule is written.

## What is enforced

`tests/Backlog.ArchitectureTests/StorybookCoverageTests.cs`:

1. every component in the library is rendered by at least one story;
2. every page is reachable from the index;
3. every `.design` chapter and anchor a page names still resolves to a heading.

`UiLibraryBoundaryTests` keeps all of that possible by proving the library
references no module, adapter, or application — a component that reads state
instead of taking a parameter cannot be rendered here at all.

Nothing yet checks that a story carries its usage, or that a page stays on its
own subject. Those are review concerns until a test claims them.

## Related

- `.design/README.md#living-reference-the-ui-storybook` — the rules.
- `.github/instructions/ui-components.instructions.md` — why the library exists
  and when a screen must adopt from it rather than grow its own copy.
