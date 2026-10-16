using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The repositories the Sprints settings page may offer a series: the registry's
/// aliases, read through Tasks' published port and nothing else.
/// </summary>
public class SprintRepositoriesTests
{
    [Fact]
    public void Answers_the_registered_aliases_in_the_registry_order()
    {
        var registry = new Registry(
            new TasksRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new TasksRepositoryRef("docs", "JSdotNet", "Backlog-docs"));

        var aliases = new SprintRepositories(() => registry).Aliases;

        Assert.Equal(["backlog", "docs"], aliases);
    }

    [Fact]
    public void Leaves_out_a_blank_alias_and_a_second_spelling_of_one_already_listed()
    {
        var registry = new Registry(
            new TasksRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new TasksRepositoryRef(" ", "JSdotNet", "Blank"),
            new TasksRepositoryRef("Backlog", "Someone", "Backlog"));

        var aliases = new SprintRepositories(() => registry).Aliases;

        Assert.Equal(["backlog"], aliases);
    }

    /// <summary>An archived repository (local ADR 0029) is still one of the aliases, since
    /// a series keeps it, and is named among the archived ones so the page can leave it
    /// out of what it offers.</summary>
    [Fact]
    public void An_archived_repository_stays_listed_and_is_named_archived()
    {
        var registry = new Registry(
            new TasksRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new TasksRepositoryRef("docs", "JSdotNet", "Backlog-docs") { IsArchived = true });
        var adapter = new SprintRepositories(() => registry);

        Assert.Equal(["backlog", "docs"], adapter.Aliases);
        Assert.Equal(["docs"], adapter.ArchivedAliases);
        Assert.Empty(new SprintRepositories(() => null).ArchivedAliases);
    }

    [Fact]
    public void A_host_with_no_repository_directory_offers_none()
    {
        Assert.Empty(new SprintRepositories(() => null).Aliases);
    }

    [Fact]
    public void Reads_the_directory_again_on_every_read()
    {
        var registry = new Registry(new TasksRepositoryRef("backlog", "JSdotNet", "Backlog"));
        var adapter = new SprintRepositories(() => registry);
        _ = adapter.Aliases;

        registry.Rows.Add(new TasksRepositoryRef("tools", "JSdotNet", "Tools"));

        Assert.Equal(["backlog", "tools"], adapter.Aliases);
    }

    private sealed class Registry(params TasksRepositoryRef[] rows) : IRepositoryDirectory
    {
        public List<TasksRepositoryRef> Rows { get; } = [.. rows];

        public IReadOnlyList<TasksRepositoryRef> Repositories => Rows;

        public TasksRepositoryRef? Resolve(string name) => Rows.FirstOrDefault(row => row.Alias == name);

        public Result<TasksRepositoryRef> Register(string name) => throw new NotSupportedException();
    }
}
