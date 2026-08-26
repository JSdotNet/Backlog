namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The hash is the only link between a mermaid fence and the Archify artifact
/// somebody authored from it, and it is written twice — here and in
/// <c>normalizeDiagramSource</c>/<c>diagramSourceHash</c> in
/// <c>tools/diagrams/archify-artifacts.mjs</c>. If the two drift, every lookup
/// misses and the app quietly shows mermaid everywhere, which is the one failure
/// this design cannot notice from the inside. So both halves are pinned: the
/// things that must not change the hash, the thing that must, and one literal
/// digest taken from the generator itself.
/// </summary>
public sealed class DiagramSourceHashTests
{
    [Fact]
    public void The_same_diagram_checked_out_with_CRLF_hashes_the_same_as_with_LF()
    {
        const string lf = "flowchart TD\n    A[Start] --> B[Stop]";
        const string crlf = "flowchart TD\r\n    A[Start] --> B[Stop]";

        Assert.Equal(DiagramSourceHash.Of(lf), DiagramSourceHash.Of(crlf));
    }

    [Fact]
    public void A_bare_carriage_return_is_a_line_ending_too()
    {
        const string lf = "flowchart TD\n    A --> B";
        const string cr = "flowchart TD\r    A --> B";

        Assert.Equal(DiagramSourceHash.Of(lf), DiagramSourceHash.Of(cr));
    }

    [Fact]
    public void Trailing_spaces_and_tabs_do_not_change_the_hash()
    {
        const string clean = "sequenceDiagram\n    A ->> B: ask\n    B -->> A: answer";
        const string padded = "sequenceDiagram   \n    A ->> B: ask\t\t\n    B -->> A: answer  \t ";

        Assert.Equal(DiagramSourceHash.Of(clean), DiagramSourceHash.Of(padded));
    }

    [Fact]
    public void Blank_lines_at_either_end_do_not_change_the_hash()
    {
        const string clean = "flowchart LR\n    A --> B";
        const string padded = "\n\n   \nflowchart LR\n    A --> B\n\n\t\n";

        Assert.Equal(DiagramSourceHash.Of(clean), DiagramSourceHash.Of(padded));
    }

    [Fact]
    public void A_blank_line_in_the_middle_is_part_of_the_diagram()
    {
        const string joined = "flowchart LR\n    A --> B\n    B --> C";
        const string split = "flowchart LR\n    A --> B\n\n    B --> C";

        Assert.NotEqual(DiagramSourceHash.Of(joined), DiagramSourceHash.Of(split));
    }

    [Fact]
    public void A_different_diagram_hashes_differently()
    {
        const string one = "flowchart TD\n    A[Start] --> B[Stop]";
        const string other = "flowchart TD\n    A[Start] --> C[Stop]";

        Assert.NotEqual(DiagramSourceHash.Of(one), DiagramSourceHash.Of(other));
    }

    [Fact]
    public void An_edited_fence_hashes_differently_from_the_one_the_artifact_was_authored_from()
    {
        const string authored = "flowchart TD\n    A[Start] --> B[Stop]";
        const string edited = "flowchart TD\n    A[Start] --> B[Stop]\n    B --> C[Archive]";

        Assert.NotEqual(DiagramSourceHash.Of(authored), DiagramSourceHash.Of(edited));
    }

    [Fact]
    public void Normalize_strips_the_endings_the_whitespace_and_the_outer_blank_lines()
    {
        Assert.Equal(
            "flowchart TD\n    A[Start] --> B[Stop]",
            DiagramSourceHash.Normalize("\r\nflowchart TD\r\n    A[Start] --> B[Stop]   \r\n\r\n"));
    }

    [Fact]
    public void Nothing_normalizes_to_nothing()
    {
        Assert.Equal(string.Empty, DiagramSourceHash.Normalize(null));
        Assert.Equal(string.Empty, DiagramSourceHash.Normalize(string.Empty));
        Assert.Equal(string.Empty, DiagramSourceHash.Normalize("\r\n  \n\t\n"));
    }

    /// <summary>
    /// The literal is what <c>diagramSourceHash</c> in
    /// <c>tools/diagrams/archify-artifacts.mjs</c> returns for this source — taken
    /// from the generator rather than from this implementation, so the assertion
    /// fails if either side is changed alone. That is the whole point of writing
    /// it down: the two normalisations are the same rule expressed twice, and no
    /// running app can tell you when they stop agreeing.
    /// </summary>
    [Fact]
    public void The_digest_is_the_one_the_generator_files_an_artifact_under()
    {
        Assert.Equal(
            "0bdea2dcc9dfb27d8404fd42bdad9b1becdb4d3a3ca9960397a03e8ce6267e72",
            DiagramSourceHash.Of("flowchart TD\r\n    A[Start] --> B[Stop]   \r\n\r\n"));
    }

    [Fact]
    public void The_digest_is_lowercase_hex_because_the_index_is_keyed_by_ordinal_string_comparison()
    {
        var hash = DiagramSourceHash.Of("flowchart TD\n    A --> B");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }
}
