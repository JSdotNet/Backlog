namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Goals view sits in the shell's column, which `.shell` already insets. Side
/// padding of its own put the title and the goal cards a step right of the repository
/// scope and of every other view.
/// </summary>
public sealed class GoalsViewLayoutTests
{
    [Fact]
    public void Goals_view_body_adds_no_side_padding_to_the_shell_inset()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"))
            .Replace("\r\n", "\n");

        var start = css.IndexOf(".goals-view__body {\n", StringComparison.Ordinal);
        Assert.True(start >= 0, ".goals-view__body should exist.");
        var body = css[start..css.IndexOf("}\n", start, StringComparison.Ordinal)];

        Assert.Contains("padding: var(--spacing-md) 0 var(--spacing-lg);", body, StringComparison.Ordinal);
    }
}
