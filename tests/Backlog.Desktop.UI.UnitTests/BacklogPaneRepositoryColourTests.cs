using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository identity mark on an entry row.
/// <para>
/// Asserted as classes rather than as a colour, because that is the whole shape of the
/// decision: the pane says <em>which</em> repository a row belongs to and the stylesheet
/// says which hue that is. A test reading a hex value here would be a test of
/// <c>components.css</c> written in the wrong project.
/// </para>
/// </summary>
public class BacklogPaneRepositoryColourTests
{
    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    [Fact]
    public async Task ARowCarriesTheMarkOfTheRepositoryItsAreaNames()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var first = await host.WriteEntryAsync("# Provision the box\n`task` `@backlog`\n");
        var second = await host.WriteEntryAsync("# Write it up\n`task` `@docs`\n");

        var pane = host.Render();

        // Position, because neither repository has been given a colour of its own.
        Assert.Contains("repo-mark--1", pane.Find($"[data-testid='{RowTestId(first)}']").ClassName);
        Assert.Contains("repo-mark--2", pane.Find($"[data-testid='{RowTestId(second)}']").ClassName);
    }

    [Fact]
    public async Task AChosenColourReachesTheRow()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `@backlog`\n");
        Assert.Null(host.GitHub.Settings.SetRepositoryColour("backlog", 4));

        var pane = host.Render();

        // The point of the choice living in Settings: the list is reading the same
        // answer the filter and the roadmap read.
        Assert.Contains("repo-mark--4", pane.Find($"[data-testid='{RowTestId(row)}']").ClassName);
    }

    [Fact]
    public async Task ARowFiledUnderNothingConfiguredWearsNoMark()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var unfiled = await host.WriteEntryAsync("# Provision the box\n`task`\n");
        var elsewhere = await host.WriteEntryAsync("# Buy milk\n`task` `@errands`\n");

        var pane = host.Render();

        // An area is a pile somebody typed, and most of them are not repositories.
        // Colouring one would be inventing a project.
        Assert.DoesNotContain("repo-mark", pane.Find($"[data-testid='{RowTestId(unfiled)}']").ClassName);
        Assert.DoesNotContain("repo-mark", pane.Find($"[data-testid='{RowTestId(elsewhere)}']").ClassName);
    }

    [Fact]
    public async Task TheMarkIsNeverTheOnlyThingSayingWhichRepository()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `@backlog`\n");

        var pane = host.Render();

        // .design/color-scheme.md#band-identity-tokens requires it: the alias stays
        // written on the row, so a reader who never sees a hue loses nothing.
        Assert.Contains("backlog", pane.Find($"[data-testid='{RowTestId(row)}']").TextContent);
    }
}
