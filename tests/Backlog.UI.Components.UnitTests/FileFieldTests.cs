namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about the file picker: that the element the platform
/// opens a dialog from is a real, reachable input carrying the host's test id,
/// and that the help and error lines are wired into the control the way every
/// other field's are.
///
/// <para>The reading itself is not pinned here. It runs through
/// <c>IBrowserFile.OpenReadStream</c>, which needs the browser behind it; what a
/// unit test can hold is the markup that decides whether a driver — or a
/// keyboard — can reach the control at all.</para>
/// </summary>
public sealed class FileFieldTests
{
    [Fact]
    public void The_test_id_lands_on_the_file_input_itself()
    {
        using var context = new BunitContext();

        var field = context.Render<FileField>(parameters => parameters
            .Add(f => f.TestId, "tools-import-file"));

        // On the input and not the wrapper, unlike TextField: a driver hands a
        // file to the <input type="file">, so an id on a div around it is one it
        // cannot use.
        var found = Assert.Single(field.FindAll("[data-testid=\"tools-import-file\"]"));
        Assert.Equal("INPUT", found.TagName);
        Assert.Equal("file", found.GetAttribute("type"));
    }

    [Fact]
    public void The_accept_filter_reaches_the_input()
    {
        using var context = new BunitContext();

        var field = context.Render<FileField>(parameters => parameters.Add(f => f.Accept, ".json"));

        Assert.Equal(".json", field.Find("input[type=file]").GetAttribute("accept"));
    }

    [Fact]
    public void Help_and_error_are_both_described_by_the_input()
    {
        using var context = new BunitContext();

        var field = context.Render<FileField>(parameters => parameters
            .Add(f => f.Label, "Catalog file")
            .Add(f => f.HelpText, "A copilot-tools.json.")
            .Add(f => f.ErrorMessage, "That is not a tool catalog."));

        var input = field.Find("input[type=file]");
        var describedBy = input.GetAttribute("aria-describedby")!.Split(' ');

        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal(2, describedBy.Length);
        Assert.All(describedBy, id => Assert.NotNull(field.Find("#" + id)));
        Assert.Contains("field--invalid", field.Find("div").GetAttribute("class"));
    }

    [Fact]
    public void A_label_is_tied_to_the_input_by_id()
    {
        using var context = new BunitContext();

        var field = context.Render<FileField>(parameters => parameters.Add(f => f.Label, "Catalog file"));

        Assert.Equal(field.Find("input[type=file]").Id, field.Find("label").GetAttribute("for"));
    }
}
