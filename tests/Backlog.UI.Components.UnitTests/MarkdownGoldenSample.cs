namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One document holding every block the read view knows how to draw.
///
/// <para>Parsed rather than hand-built, unlike the knowledge samples: these
/// blocks are what <c>MarkdownPreview</c> actually produces, and the point of a
/// golden rendering is to pin the markup a real body turns into.</para>
/// </summary>
internal static class MarkdownGoldenSample
{
    internal static IReadOnlyList<MdBlock> Blocks => MarkdownPreview.ParseDocument(Body);

    private const string Body = """
        # A heading

        A paragraph with **bold**, *emphasis*, `inline code`, a #tag and a
        [link](https://example.com), and a footnote[^note].

        - A bullet
          - Nested under it
          1. Numbers under a bullet
        - [x] A finished task
        - [ ] An unfinished one
        - A plain bullet in the same list

        1. First ordered item
        2. Second ordered item

        > A quote, which may run
        > over more than one line.

        | Left | Middle | Right |
        | :--- | :----: | ----: |
        | one  | two    | three |
        | four | five   |       |

        ```csharp
        var blocks = MarkdownPreview.ParseDocument(source);
        ```

        ```mermaid
        graph TD; a-->b;
        ```

        ---

        [^note]: Notes are collected at the bottom.
        """;
}
