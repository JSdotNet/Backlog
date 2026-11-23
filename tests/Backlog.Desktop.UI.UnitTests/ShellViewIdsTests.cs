using Backlog.Infrastructure.FileSystem;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shell keeps its main view in <c>shell-navigation.json</c> as a surface id now
/// (local ADR 0043 §2). A file written while the retired <c>ShellView</c> enum named the
/// view holds the member's name — or <c>InProgress</c>, the Goals view's name before its
/// rename (local ADR 0035 §10) — and still reopens on that view.
/// </summary>
public sealed class ShellViewIdsTests
{
    /// <summary>Every name the retired enum ever stored, with the id it reads as.</summary>
    [Theory]
    [InlineData("Tasks", "tasks")]
    [InlineData("Roadmap", "roadmap")]
    [InlineData("Calendar", "calendar")]
    [InlineData("Board", "board")]
    [InlineData("Goals", "goals")]
    [InlineData("InProgress", "goals")]
    public void A_view_stored_under_an_old_enum_name_restores_as_its_id(string stored, string id)
    {
        using var temp = new TemporaryDirectory("backlog-shell-view-ids-tests");
        var path = WriteFile(temp, $$"""{ "lastSurface": "Workspace", "lastView": "{{stored}}" }""");

        var store = new ShellNavigationStore(path);

        Assert.Equal(id, store.LastView);

        // And the id is what the next save writes, so the old name leaves the file.
        store.SetRepositoryScope(["docs"]);
        store.Flush();
        Assert.Contains($"\"lastView\": \"{id}\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>Every old name is mapped: a test above for each, and no other.</summary>
    [Fact]
    public void The_retired_names_are_exactly_the_old_enum_members_and_in_progress() =>
        Assert.Equal(
            ["Board", "Calendar", "Goals", "InProgress", "Roadmap", "Tasks"],
            ShellNavigationStore.RetiredViewNames.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public void An_old_name_handed_to_set_last_view_is_kept_as_its_id()
    {
        using var temp = new TemporaryDirectory("backlog-shell-view-ids-tests");
        var path = WriteFile(temp, "{}");

        var store = new ShellNavigationStore(path);
        store.SetLastView("Board");

        Assert.Equal("board", store.LastView);
        Assert.Equal("board", new ShellNavigationStore(path).LastView);
    }

    /// <summary>An id, or a name no view had — a newer build's — is kept as written; a
    /// different case of an old name is not one of them. The shell reads an id no view
    /// registered as Tasks.</summary>
    [Theory]
    [InlineData("tasks")]
    [InlineData("goals")]
    [InlineData("timeline")]
    [InlineData("inprogress")]
    public void Anything_else_is_kept_as_written(string stored)
    {
        using var temp = new TemporaryDirectory("backlog-shell-view-ids-tests");
        var path = WriteFile(temp, $$"""{ "lastView": "{{stored}}" }""");

        Assert.Equal(stored, new ShellNavigationStore(path).LastView);
    }

    [Fact]
    public void A_file_with_no_view_reopens_on_tasks()
    {
        using var temp = new TemporaryDirectory("backlog-shell-view-ids-tests");
        var path = WriteFile(temp, "{}");

        Assert.Equal("tasks", new ShellNavigationStore(path).LastView);
    }

    private static string WriteFile(TemporaryDirectory temp, string json)
    {
        var path = temp.Combine("shell-navigation.json");
        File.WriteAllText(path, json);
        return path;
    }
}
