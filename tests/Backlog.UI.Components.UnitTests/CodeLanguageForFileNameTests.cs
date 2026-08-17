namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Reading a language off a file's name. This is what decides whether FileView
/// shows a file as code or as prose, so what it declines to recognise matters as
/// much as what it does.
/// </summary>
public sealed class CodeLanguageForFileNameTests
{
    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("push.ts", "typescript")]
    [InlineData("components.js", "javascript")]
    [InlineData("appsettings.json", "json")]
    [InlineData("dependabot.yml", "yaml")]
    [InlineData("components.css", "css")]
    [InlineData("index.html", "html")]
    [InlineData("Backlog.UI.Components.csproj", "xml")]
    [InlineData("seed.sql", "sql")]
    [InlineData("build.sh", "bash")]
    [InlineData("sync.ps1", "powershell")]
    [InlineData("lifecycle.mmd", "mermaid")]
    public void An_extension_names_a_language_the_same_way_a_fence_does(string fileName, string expected)
    {
        Assert.Equal(expected, CodeLanguages.ForFileName(fileName));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("notes.txt")]
    [InlineData("context-loading.instructions.md")]
    public void A_file_that_is_already_readable_is_not_a_language(string fileName)
    {
        // Not an omission: these render as markdown, which is the whole point of
        // returning null here.
        Assert.Null(CodeLanguages.ForFileName(fileName));
    }

    [Theory]
    [InlineData("Dockerfile")]
    [InlineData(".gitignore")]
    [InlineData("LICENSE")]
    [InlineData("trailing.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_name_with_no_usable_extension_names_nothing(string? fileName)
    {
        Assert.Null(CodeLanguages.ForFileName(fileName));
    }

    [Theory]
    [InlineData(@"src\UI\Backlog.UI.Components\Code\CodeView.razor")]
    [InlineData("src/UI/Backlog.UI.Components/Code/CodeView.razor")]
    public void A_path_is_read_from_its_last_segment(string path)
    {
        // `.razor` is not on the list, and the `.Components` folder above it must
        // not be allowed to answer for the file.
        Assert.Null(CodeLanguages.ForFileName(path));
    }

    [Theory]
    [InlineData(@"src\harness\Backlog.UI.Storybook\Program.cs")]
    [InlineData("src/harness/Backlog.UI.Storybook/Program.cs")]
    public void A_full_path_still_finds_the_language_of_the_file_at_the_end_of_it(string path)
    {
        Assert.Equal("csharp", CodeLanguages.ForFileName(path));
    }

    [Fact]
    public void An_extension_is_read_the_way_it_is_written_or_not()
    {
        Assert.Equal("csharp", CodeLanguages.ForFileName("Program.CS"));
        Assert.Equal("yaml", CodeLanguages.ForFileName("Deploy.YAML"));
    }
}
