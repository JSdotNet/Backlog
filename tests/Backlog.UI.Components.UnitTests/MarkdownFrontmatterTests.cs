namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The frontmatter reader, against the shapes this repository's own instruction
/// files are written in — and against the ones they are not, because a file that
/// says nothing in its first four lines has to come back with its text untouched
/// rather than four lines lighter.
/// </summary>
public sealed class MarkdownFrontmatterTests
{
    /// <summary>Verbatim from .github/instructions/ui-components.instructions.md,
    /// which is where the comma-separated glob list comes from.</summary>
    private const string UiComponents = """
        ---
        applyTo: "src/App/**,src/Modules/**,src/Core/Backlog.UI.Components/**"
        description: An application screen renders the shared component library's components rather than growing its own copies; how to make a component fit, and what to do when it cannot.
        ---

        # UI components

        `src/Core/Backlog.UI.Components` is the product's own component library.
        """;

    [Fact]
    public void A_real_instruction_file_reads_back_as_its_globs_and_its_description()
    {
        var frontmatter = MarkdownFrontmatter.Read(UiComponents);

        Assert.Equal(
            ["src/App/**", "src/Modules/**", "src/Core/Backlog.UI.Components/**"],
            frontmatter.ApplyTo);
        Assert.StartsWith(
            "An application screen renders the shared component library's components",
            frontmatter.Description,
            StringComparison.Ordinal);
        Assert.False(frontmatter.IsEmpty);
    }

    [Fact]
    public void The_body_comes_back_without_the_block_and_otherwise_verbatim()
    {
        var frontmatter = MarkdownFrontmatter.Read(UiComponents);

        // One blank line, which is the one the file had between its block and its
        // heading: what was taken out is the block and the two fence lines.
        Assert.Equal(
            """

            # UI components

            `src/Core/Backlog.UI.Components` is the product's own component library.
            """,
            frontmatter.Body);
    }

    [Fact]
    public void A_description_keeps_the_colons_and_commas_it_was_written_with()
    {
        // Split on the first colon only: the rest belongs to the prose.
        var frontmatter = MarkdownFrontmatter.Read("""
            ---
            description: The gate, literally: size is not a criterion (nor is a missing spec).
            ---
            """);

        Assert.Equal("The gate, literally: size is not a criterion (nor is a missing spec).", frontmatter.Description);
    }

    [Fact]
    public void A_quoted_description_loses_the_quotes_and_nothing_else()
    {
        var single = MarkdownFrontmatter.Read("---\ndescription: 'Aspire's own words, quoted.'\n---\n");
        var doubled = MarkdownFrontmatter.Read("---\ndescription: \"Quoted the other way.\"\n---\n");

        Assert.Equal("Aspire's own words, quoted.", single.Description);
        Assert.Equal("Quoted the other way.", doubled.Description);
    }

    [Fact]
    public void One_glob_is_one_badge_worth_of_glob()
    {
        // `applyTo: "**"` is the whole repository, and it is a single entry.
        var frontmatter = MarkdownFrontmatter.Read("---\napplyTo: \"**\"\n---\n# Title\n");

        Assert.Equal(["**"], frontmatter.ApplyTo);
        Assert.Equal("# Title\n", frontmatter.Body);
    }

    [Fact]
    public void Tools_are_read_from_the_inline_list_and_from_the_dash_block_alike()
    {
        // Neither shape appears in this repository yet; both are what the
        // convention writes elsewhere, so both are pinned here rather than
        // discovered by the first file that carries one.
        var inline = MarkdownFrontmatter.Read("---\ntools: ['codebase', 'search/codebase']\n---\n");
        var block = MarkdownFrontmatter.Read("""
            ---
            tools:
              - codebase
              - search/codebase
            ---
            """);

        Assert.Equal(["codebase", "search/codebase"], inline.Tools);
        Assert.Equal(["codebase", "search/codebase"], block.Tools);
    }

    [Fact]
    public void Every_field_at_once_reads_back_whatever_order_it_was_written_in()
    {
        var frontmatter = MarkdownFrontmatter.Read("""
            ---
            tools: [codebase]
            mode: agent
            description: Everything at once.
            applyTo: "**/*.cs"
            ---

            # Title
            """);

        Assert.Equal("Everything at once.", frontmatter.Description);
        Assert.Equal(["**/*.cs"], frontmatter.ApplyTo);
        Assert.Equal(["codebase"], frontmatter.Tools);
    }

    [Fact]
    public void A_key_with_no_field_of_its_own_still_comes_back()
    {
        // `mode` and `model` are real prompt-file keys and neither has a field
        // here. They are still what the file says, and the block does leave the
        // body — so a reader who could not see them here would be reading a file
        // two lines shorter than the one on disk, with nowhere to find that out.
        var frontmatter = MarkdownFrontmatter.Read("---\nmode: agent\nmodel: Claude Opus 5\n---\n\n# Prompt\n");

        Assert.False(frontmatter.IsEmpty);
        Assert.Equal(["mode", "model"], frontmatter.Other.Select(field => field.Key));
        Assert.Equal(["agent", "Claude Opus 5"], frontmatter.Other.Select(field => field.Text));
        Assert.Equal("\n# Prompt\n", frontmatter.Body);
    }

    [Fact]
    public void A_skill_files_name_reads_beside_its_description()
    {
        // The shape of .github/skills/pr-jsdotnet/SKILL.md: a `name` no field here
        // knows about, and a single-quoted description.
        var frontmatter = MarkdownFrontmatter.Read("""
            ---
            name: pr-jsdotnet
            description: 'Create a GitHub Pull Request in any JSdotNet repository through the `gh` CLI.'
            ---

            # Create PR in JSdotNet Repositories
            """);

        Assert.Equal("Create a GitHub Pull Request in any JSdotNet repository through the `gh` CLI.", frontmatter.Description);

        var name = Assert.Single(frontmatter.Other);

        Assert.Equal("name", name.Key);
        Assert.Equal("Name", name.Label);
        Assert.Equal("pr-jsdotnet", name.Text);
    }

    [Fact]
    public void The_leftover_keys_keep_the_order_the_file_wrote_them_in()
    {
        // The order is read off the block's own lines because the shared reader
        // hands back a dictionary — and this block is exactly the case that makes
        // one lose its insertion order: `related: []` states nothing, so the
        // reader removes a key it had already added.
        var frontmatter = MarkdownFrontmatter.Read("""
            ---
            model: Claude Opus 5
            related: []
            name: pr-jsdotnet
            mode: agent
            ---
            """);

        Assert.Equal(["model", "name", "mode"], frontmatter.Other.Select(field => field.Key));
    }

    [Fact]
    public void A_leftover_key_holding_a_list_is_one_row_of_several_things()
    {
        var frontmatter = MarkdownFrontmatter.Read("---\nagent-names: [claude, copilot]\n---\n");

        var field = Assert.Single(frontmatter.Other);

        Assert.Equal("Agent names", field.Label);
        Assert.Equal(["claude", "copilot"], field.Values);
        Assert.Equal("claude, copilot", field.Text);
    }

    [Fact]
    public void A_key_with_no_value_states_nothing_and_is_not_a_row()
    {
        // `mode:` with nothing after it, `null` and `[]` all read as "not stated" —
        // the same reading the `meta` fence gives them. There is nothing to put
        // beside a label, and a block of nothing but those keeps its place in the
        // body rather than being taken out for a strip with no rows in it.
        var stated = MarkdownFrontmatter.Read("---\ndescription: Stated.\nmode:\nmodel: null\nrelated: []\n---\n\n# Prompt\n");
        var nothing = MarkdownFrontmatter.Read("---\nmode:\nmodel: null\n---\n\n# Prompt\n");

        Assert.Equal("Stated.", stated.Description);
        Assert.Empty(stated.Other);

        Assert.True(nothing.IsEmpty);
        Assert.Equal("---\nmode:\nmodel: null\n---\n\n# Prompt\n", nothing.Body);
    }

    [Fact]
    public void The_block_leaves_the_body_exactly_when_there_is_something_to_show()
    {
        // The invariant both callers lean on, in one place.
        var drawn = MarkdownFrontmatter.Read("---\nname: pr-jsdotnet\n---\n\n# Skill\n");
        var silent = MarkdownFrontmatter.Read("---\n---\n\n# Skill\n");

        Assert.False(drawn.IsEmpty);
        Assert.Equal("\n# Skill\n", drawn.Body);

        Assert.True(silent.IsEmpty);
        Assert.Equal("---\n---\n\n# Skill\n", silent.Body);
    }

    [Fact]
    public void A_file_that_opens_with_prose_has_no_frontmatter_to_find()
    {
        var frontmatter = MarkdownFrontmatter.Read("# UI components\n\nA paragraph.\n\n---\n\nA divider, further down.\n");

        Assert.True(frontmatter.IsEmpty);
        Assert.Equal("# UI components\n\nA paragraph.\n\n---\n\nA divider, further down.\n", frontmatter.Body);
    }

    [Fact]
    public void An_unterminated_block_is_a_divider_and_not_a_record()
    {
        // Reading on to the end of the file would swallow the whole document as
        // metadata on the strength of one line.
        var frontmatter = MarkdownFrontmatter.Read("---\ndescription: Never closed.\n\n# Title\n\nA paragraph.\n");

        Assert.True(frontmatter.IsEmpty);
        Assert.Null(frontmatter.Description);
        Assert.Equal("---\ndescription: Never closed.\n\n# Title\n\nA paragraph.\n", frontmatter.Body);
    }

    [Fact]
    public void An_empty_block_states_nothing_and_costs_the_file_nothing()
    {
        var frontmatter = MarkdownFrontmatter.Read("---\n---\n\n# Title\n");

        Assert.True(frontmatter.IsEmpty);
        Assert.Equal("---\n---\n\n# Title\n", frontmatter.Body);
    }

    [Fact]
    public void Nothing_at_all_reads_back_as_nothing_at_all()
    {
        Assert.True(MarkdownFrontmatter.Read(null).IsEmpty);
        Assert.Null(MarkdownFrontmatter.Read(null).Body);
        Assert.Equal(string.Empty, MarkdownFrontmatter.Read(string.Empty).Body);
        Assert.Equal("   \n", MarkdownFrontmatter.Read("   \n").Body);
    }

    [Fact]
    public void A_file_written_with_windows_line_endings_reads_the_same_way()
    {
        // Every instruction file in a clone on this platform is CRLF, so this is
        // the shape the feature actually meets — and the body keeps its own
        // endings rather than being quietly rewritten to LF.
        var frontmatter = MarkdownFrontmatter.Read("---\r\napplyTo: \"src/App/**,src/Modules/**\"\r\ndescription: Windows all the way down.\r\n---\r\n\r\n# Title\r\n");

        Assert.Equal(["src/App/**", "src/Modules/**"], frontmatter.ApplyTo);
        Assert.Equal("Windows all the way down.", frontmatter.Description);
        Assert.Equal("\r\n# Title\r\n", frontmatter.Body);
    }

    [Fact]
    public void A_four_dash_line_is_a_rule_somebody_drew()
    {
        var frontmatter = MarkdownFrontmatter.Read("----\ndescription: Not frontmatter.\n----\n");

        Assert.True(frontmatter.IsEmpty);
        Assert.Equal("----\ndescription: Not frontmatter.\n----\n", frontmatter.Body);
    }
}
