using Backlog.Desktop.Services;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The desktop head's answer to a module script request, held to serving every
/// <c>.mjs</c> its own origin asks for as JavaScript and declining everything
/// else.
/// <para>
/// MAUI's Windows BlazorWebView serves app files from its own copy of the
/// extension table, and that copy has no <c>.mjs</c>: the vendored Mermaid
/// entry and its chunks went out as <c>application/octet-stream</c>, WebView2
/// refused to run them as modules, and every diagram read "Mermaid renderer
/// unavailable." The web harnesses map <c>.mjs</c> correctly, so no browser
/// test could see it; this is the regression test for that head.
/// </para>
/// </summary>
public sealed class ModuleScriptResponsesTests
{
    // The head's own origin, so the tests pin the value MainPage passes.
    private static readonly Uri AppOrigin = ModuleScriptResponses.AppOrigin;

    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "backlog-head", "wwwroot");

    private static string UnderRoot(params string[] segments) => Path.Combine([ContentRoot, .. segments]);

    [Fact]
    public void The_mermaid_entry_is_served_from_the_heads_wwwroot()
    {
        var served = ModuleScriptResponses.TryResolve(
            AppOrigin,
            "https://0.0.0.1/_content/Backlog.UI.Components/lib/mermaid/mermaid.esm.min.mjs",
            ContentRoot,
            out var filePath);

        Assert.True(served);
        Assert.Equal(UnderRoot("_content", "Backlog.UI.Components", "lib", "mermaid", "mermaid.esm.min.mjs"), filePath);
    }

    [Fact]
    public void A_retried_load_is_served_from_the_same_file()
    {
        var served = ModuleScriptResponses.TryResolve(
            AppOrigin,
            "https://0.0.0.1/_content/Backlog.UI.Components/lib/mermaid/mermaid.esm.min.mjs?retry=1",
            ContentRoot,
            out var filePath);

        Assert.True(served);
        Assert.Equal(UnderRoot("_content", "Backlog.UI.Components", "lib", "mermaid", "mermaid.esm.min.mjs"), filePath);
    }

    [Fact]
    public void A_chunk_the_entry_imports_is_served_from_its_nested_folder()
    {
        var served = ModuleScriptResponses.TryResolve(
            AppOrigin,
            "https://0.0.0.1/_content/Backlog.UI.Components/lib/mermaid/chunks/mermaid.esm.min/chunk-ABC123.mjs",
            ContentRoot,
            out var filePath);

        Assert.True(served);
        Assert.Equal(UnderRoot("_content", "Backlog.UI.Components", "lib", "mermaid", "chunks", "mermaid.esm.min", "chunk-ABC123.mjs"), filePath);
    }

    [Fact]
    public void The_extension_is_matched_regardless_of_case()
    {
        var served = ModuleScriptResponses.TryResolve(AppOrigin, "https://0.0.0.1/lib/Entry.MJS", ContentRoot, out var filePath);

        Assert.True(served);
        Assert.Equal(UnderRoot("lib", "Entry.MJS"), filePath);
    }

    [Fact]
    public void A_percent_encoded_segment_names_the_file_on_disk()
    {
        var served = ModuleScriptResponses.TryResolve(AppOrigin, "https://0.0.0.1/lib/my%20module.mjs", ContentRoot, out var filePath);

        Assert.True(served);
        Assert.Equal(UnderRoot("lib", "my module.mjs"), filePath);
    }

    [Theory]
    [InlineData("https://0.0.0.1/_content/Backlog.UI.Components/components.js")]
    [InlineData("https://0.0.0.1/css/app.css")]
    [InlineData("https://0.0.0.1/index.html")]
    [InlineData("https://0.0.0.1/devbook/tech")]
    [InlineData("https://0.0.0.1/")]
    [InlineData("https://0.0.0.1/lib/entry.mjs.map")]
    public void Every_other_app_file_is_left_to_MAUI(string requestUri)
    {
        var served = ModuleScriptResponses.TryResolve(AppOrigin, requestUri, ContentRoot, out var filePath);

        Assert.False(served);
        Assert.Null(filePath);
    }

    [Theory]
    [InlineData("https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs")]
    [InlineData("http://0.0.0.1/lib/entry.mjs")]
    [InlineData("https://0.0.0.1:8443/lib/entry.mjs")]
    [InlineData("/lib/entry.mjs")]
    public void A_module_from_another_origin_is_left_to_WebView2(string requestUri)
    {
        var served = ModuleScriptResponses.TryResolve(AppOrigin, requestUri, ContentRoot, out var filePath);

        Assert.False(served);
        Assert.Null(filePath);
    }

    [Theory]
    [InlineData("https://0.0.0.1/lib/%2E%2E%2F%2E%2E%2Fsecrets.mjs")]
    [InlineData("https://0.0.0.1/lib/..%5C..%5Csecrets.mjs")]
    [InlineData("https://0.0.0.1/C:%5CWindows%5Csecrets.mjs")]
    public void A_path_that_escapes_wwwroot_is_never_served(string requestUri)
    {
        var served = ModuleScriptResponses.TryResolve(AppOrigin, requestUri, ContentRoot, out var filePath);

        Assert.False(served);
        Assert.Null(filePath);
    }

    // MAUI raises the hook inside an async-void lambda, so a throw here would
    // take the whole app down: a path that names no file is declined instead.
    [Theory]
    [InlineData("https://0.0.0.1/lib/a%00.mjs")]
    public void A_path_that_cannot_name_a_file_is_declined(string requestUri)
    {
        var served = ModuleScriptResponses.TryResolve(AppOrigin, requestUri, ContentRoot, out var filePath);

        Assert.False(served);
        Assert.Null(filePath);
    }

    [Fact]
    public void A_served_module_is_typed_as_JavaScript_and_never_cached()
    {
        Assert.Equal("text/javascript", ModuleScriptResponses.Headers["Content-Type"]);
        Assert.Equal("no-cache, max-age=0, must-revalidate, no-store", ModuleScriptResponses.Headers["Cache-Control"]);
    }
}
