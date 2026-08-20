namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The highlighter is allowed to be wrong about what a snippet means; it is not
/// allowed to be wrong about what it says. Every test here is either about the
/// source surviving intact or about the handful of distinctions a reader
/// actually relies on.
/// </summary>
public sealed class CodeHighlighterTests
{
    [Theory]
    [InlineData("csharp", "public Task<int> Run() => Task.FromResult(1); // note\n/* block\n   comment */\nvar path = @\"C:\\temp\\\";")]
    [InlineData("json", "{ \"name\": \"backlog\", \"count\": 3, \"ok\": true }")]
    [InlineData("html", "<p class=\"md-p\">a &lt; b</p><!-- note -->")]
    [InlineData("css", ".code-view__body { color: var(--code-plain); padding: 1rem; } /* note */")]
    [InlineData("yaml", "title: Backlog # note\ntags:\n  - ui\ndone: false")]
    [InlineData("bash", "export PATH=\"$HOME/bin:$PATH\" # note")]
    [InlineData("mermaid", "%% note\nflowchart LR\n    A[Refine] --> B([Done])")]
    [InlineData("plaintext", "nothing here is a language")]
    public void The_tokens_put_back_together_are_the_source_they_came_from(string language, string source)
    {
        // Colour is the only thing this component adds. If concatenating the
        // tokens ever stops giving the source back, the block is lying about
        // what it contains, and a reader copying it out of the page gets
        // something that does not compile.
        var rebuilt = string.Concat(CodeHighlighter.Highlight(source, language).Select(token => token.Text));

        Assert.Equal(source, rebuilt);
    }

    [Fact]
    public void A_language_nothing_here_knows_renders_as_one_plain_run()
    {
        var token = Assert.Single(CodeHighlighter.Highlight("(defn greet [name] name)", "clojure"));

        Assert.Equal(CodeTokenKind.Plain, token.Kind);
        Assert.Equal("(defn greet [name] name)", token.Text);
    }

    [Fact]
    public void Keywords_types_strings_numbers_and_comments_are_told_apart()
    {
        var tokens = CodeHighlighter.Highlight("var count = 42; // how many\nstring name = \"ada\";", "csharp");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Keyword, Text: "var" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Number, Text: "42" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Comment, Text: "// how many" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.String, Text: "\"ada\"" });
    }

    [Fact]
    public void An_unknown_word_that_is_capitalised_reads_as_a_type()
    {
        // C# has no list of every type there is, and a snippet is not compiled
        // here anyway, so the convention is the only signal available.
        var tokens = CodeHighlighter.Highlight("TaskItem entry = new();", "csharp");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Type, Text: "TaskItem" });
    }

    [Fact]
    public void A_verbatim_string_ends_at_its_quote_and_not_at_a_backslash()
    {
        // Read with C-style escapes, the trailing \" of @"C:\temp\" opens a
        // string that swallows the rest of the file.
        var tokens = CodeHighlighter.Highlight("var path = @\"C:\\temp\\\";\nvar next = 1;", "csharp");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.String, Text: "@\"C:\\temp\\\"" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Keyword, Text: "var" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Number, Text: "1" });
    }

    [Fact]
    public void An_unterminated_string_stops_at_the_end_of_its_line()
    {
        // Half-typed code is normal in a backlog entry; it must not paint
        // everything below it.
        var tokens = CodeHighlighter.Highlight("var name = \"unfinished\nvar count = 2;", "csharp");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.String, Text: "\"unfinished" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Number, Text: "2" });
    }

    [Fact]
    public void A_comment_pressed_against_punctuation_is_still_a_comment()
    {
        var tokens = CodeHighlighter.Highlight("count = 1;// no space", "csharp");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Comment, Text: "// no space" });
    }

    [Fact]
    public void A_json_key_is_not_coloured_like_its_value()
    {
        var tokens = CodeHighlighter.Highlight("{ \"title\": \"Ship it\" }", "json");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Attribute, Text: "\"title\"" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.String, Text: "\"Ship it\"" });
    }

    [Fact]
    public void Sql_keywords_are_keywords_in_whichever_case_they_were_written()
    {
        var tokens = CodeHighlighter.Highlight("SELECT id FROM entries where done = 1", "sql");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Keyword, Text: "SELECT" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Keyword, Text: "where" });
    }

    [Fact]
    public void A_shell_variable_is_one_token_and_a_hash_starts_a_comment()
    {
        var tokens = CodeHighlighter.Highlight("cd $HOME # go home", "bash");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Type, Text: "$HOME" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Comment, Text: "# go home" });
    }

    [Fact]
    public void Mermaid_colours_the_diagram_keywords_and_leaves_the_arrows_as_punctuation()
    {
        // The picture is DiagramView's job; this is the same text while it is
        // still being written.
        var tokens = CodeHighlighter.Highlight("%% a note\nflowchart LR\n    A[Refine] --> B([Done])", "mmd");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Comment, Text: "%% a note" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Keyword, Text: "flowchart" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Type, Text: "LR" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Operator, Text: "-->" });
    }

    [Fact]
    public void Markup_separates_the_element_from_its_attributes_and_their_values()
    {
        var tokens = CodeHighlighter.Highlight("<section class=\"sb-page\">text</section>", "html");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Tag, Text: "<section" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Attribute, Text: "class" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.String, Text: "\"sb-page\"" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Plain, Text: "text" });
    }

    [Fact]
    public void A_less_than_sign_in_prose_does_not_open_an_element()
    {
        // Otherwise everything after it up to the next `>` is painted as a tag.
        var tokens = CodeHighlighter.Highlight("<p>a < b and c > d</p>", "html");

        Assert.DoesNotContain(tokens, t => t.Kind == CodeTokenKind.Attribute);
        Assert.Equal(2, tokens.Count(t => t is { Kind: CodeTokenKind.Tag, Text: "<p" } or { Kind: CodeTokenKind.Tag, Text: "</p" }));
    }

    [Fact]
    public void Css_tells_the_selector_from_the_property_from_the_value()
    {
        var tokens = CodeHighlighter.Highlight(".code-view__body { color: #F2C14E; padding: 1rem; }", "css");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Type, Text: ".code-view__body" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Keyword, Text: "color" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Number, Text: "#F2C14E" });
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Number, Text: "1rem" });
    }

    [Fact]
    public void A_yaml_key_is_a_key_and_a_colon_in_a_url_is_not()
    {
        var tokens = CodeHighlighter.Highlight("home: https://example.com/a\ndone: true", "yaml");

        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Attribute, Text: "home" });
        // The url is plain, and plain runs merge with the whitespace around
        // them, so what matters is that it is uncoloured and unbroken.
        Assert.Contains(tokens, t => t.Kind == CodeTokenKind.Plain && t.Text.Contains("https://example.com/a", StringComparison.Ordinal));
        Assert.DoesNotContain(tokens, t => t.Kind == CodeTokenKind.Attribute && t.Text.StartsWith("https", StringComparison.Ordinal));
        Assert.Contains(tokens, t => t is { Kind: CodeTokenKind.Keyword, Text: "true" });
    }

    [Fact]
    public void A_token_that_spans_lines_is_cut_at_every_one_of_them()
    {
        var lines = CodeHighlighter.HighlightLines("/* one\n   two */\nvar x = 1;", "csharp");

        Assert.Equal(3, lines.Count);
        Assert.All(lines.Take(2), line => Assert.All(line.Tokens, token => Assert.Equal(CodeTokenKind.Comment, token.Kind)));
        Assert.Equal([1, 2, 3], lines.Select(line => line.Number));
    }

    [Fact]
    public void A_blank_line_inside_a_snippet_survives_and_a_trailing_newline_does_not()
    {
        // The blank line is the author's spacing; the trailing newline is an
        // artefact of writing the snippet as a raw string literal.
        var lines = CodeHighlighter.HighlightLines("first\n\nthird\n", "csharp");

        Assert.Equal(3, lines.Count);
        Assert.Empty(lines[1].Tokens);
    }

    [Fact]
    public void Windows_line_endings_do_not_leave_a_stray_carriage_return_in_the_block()
    {
        var lines = CodeHighlighter.HighlightLines("var a = 1;\r\nvar b = 2;", "csharp");

        Assert.Equal(2, lines.Count);
        Assert.DoesNotContain(lines.SelectMany(line => line.Tokens), token => token.Text.Contains('\r'));
    }

    [Theory]
    [InlineData("cs", "csharp")]
    [InlineData("C#", "csharp")]
    [InlineData(" TS ", "typescript")]
    [InlineData("yml", "yaml")]
    [InlineData("shell", "bash")]
    public void A_fence_is_understood_by_whichever_name_it_used(string written, string expected)
    {
        Assert.Equal(expected, CodeLanguages.Resolve(written));
    }

    [Fact]
    public void An_unrecognised_language_keeps_the_name_it_was_given()
    {
        Assert.Equal("clojure", CodeLanguages.Label("clojure"));
        Assert.False(CodeLanguages.IsSupported("clojure"));
        Assert.Equal("C#", CodeLanguages.Label("cs"));
    }

    [Fact]
    public void Every_language_on_the_list_actually_has_a_grammar()
    {
        // The list is what the storybook renders, so a name on it with nothing
        // behind it would show up as a page of plain text.
        Assert.All(CodeLanguages.All, language => Assert.True(CodeLanguages.IsSupported(language.Id)));
        Assert.All(CodeLanguages.All, language => Assert.Equal(language.Label, CodeLanguages.Label(language.Id)));
    }
}
