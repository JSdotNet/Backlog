namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The globs this has to answer for are the ones the repository actually writes
/// in <c>applyTo</c> and <c>paths</c>, so those spellings are the cases here.
/// </summary>
public sealed class InstructionGlobTests
{
    [Theory]
    [InlineData("**", "CLAUDE.md")]
    [InlineData("**", "src/App/Backlog.Desktop.UI/Home.razor")]
    [InlineData("src/App/**", "src/App/Backlog.Desktop.UI/Home.razor")]
    [InlineData("src/Harness/Backlog.UI.Storybook/**", "src/Harness/Backlog.UI.Storybook/Components/Pages/Index.razor")]
    [InlineData("src/**/*.cs", "src/App/Program.cs")]
    [InlineData("*.md", "README.md")]
    [InlineData("src/App/?.cs", "src/App/A.cs")]
    public void Claims_a_path_it_covers(string glob, string path) =>
        Assert.True(InstructionGlob.Matches(glob, path));

    [Theory]
    [InlineData("src/App/**", "src/Modules/Knowledge/Thing.cs")]
    [InlineData("src/Harness/Backlog.UI.Storybook/**", "src/Harness/Backlog.Desktop.WebHarness/Program.cs")]
    [InlineData("*.md", "docs/guide.md")]
    [InlineData("src/*.cs", "src/App/Program.cs")]
    [InlineData("src/App/?.cs", "src/App/Ab.cs")]
    public void Declines_a_path_it_does_not_cover(string glob, string path) =>
        Assert.False(InstructionGlob.Matches(glob, path));

    /// <summary>
    /// The shallow case. <c>src/**/*.cs</c> has to claim <c>src/a.cs</c> as well
    /// as <c>src/deep/a.cs</c>: the separator belongs to what the double star
    /// swallows, and a translation that keeps it mandatory quietly excludes every
    /// file sitting directly in the named folder.
    /// </summary>
    [Fact]
    public void A_double_star_swallows_the_separator_with_it()
    {
        Assert.True(InstructionGlob.Matches("src/**/*.cs", "src/Program.cs"));
        Assert.True(InstructionGlob.Matches("src/**/*.cs", "src/App/Deep/Program.cs"));
    }

    /// <summary>A single star stops at a separator and a double star does not —
    /// the difference the two spellings exist for.</summary>
    [Fact]
    public void A_single_star_stays_inside_one_segment()
    {
        Assert.True(InstructionGlob.Matches("src/*/Program.cs", "src/App/Program.cs"));
        Assert.False(InstructionGlob.Matches("src/*/Program.cs", "src/App/Deep/Program.cs"));
        Assert.True(InstructionGlob.Matches("src/**/Program.cs", "src/App/Deep/Program.cs"));
    }

    [Fact]
    public void Any_glob_in_the_set_is_enough()
    {
        string[] globs = ["src/App/**", "src/Modules/**", "src/Core/Backlog.UI.Components/**"];

        Assert.True(InstructionGlob.Matches(globs, "src/Modules/Knowledge/Thing.razor"));
        Assert.False(InstructionGlob.Matches(globs, "tests/Backlog.ArchitectureTests/Thing.cs"));
    }

    /// <summary>The separator the file system used is not the separator the
    /// instruction files are written in.</summary>
    [Fact]
    public void A_windows_path_is_read_as_the_glob_is_written() =>
        Assert.True(InstructionGlob.Matches("src/App/**", @"src\App\Backlog.Desktop.UI\Home.razor"));

    [Fact]
    public void Case_does_not_decide_it() =>
        Assert.True(InstructionGlob.Matches("src/app/**", "src/App/Home.razor"));

    [Fact]
    public void A_dot_is_a_dot_and_not_any_character() =>
        Assert.False(InstructionGlob.Matches("*.md", "READMExmd"));

    [Theory]
    [InlineData(null, "src/App/Home.razor")]
    [InlineData("", "src/App/Home.razor")]
    [InlineData("   ", "src/App/Home.razor")]
    [InlineData("**", null)]
    [InlineData("**", "")]
    public void Nothing_stated_claims_nothing(string? glob, string? path) =>
        Assert.False(InstructionGlob.Matches(glob, path));
}
