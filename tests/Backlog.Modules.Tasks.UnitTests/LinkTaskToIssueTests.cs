using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.LinkTaskToIssue;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Recording that an entry became something outside this system — today a GitHub
/// issue.
/// <para>
/// The one place <c>repo_ids</c> is written outside the text path, which is why
/// it is worth its own test: everything else that touches the field goes through
/// the parser and the resolver, and this does not.
/// </para>
/// </summary>
public class LinkTaskToIssueTests
{
    /// <summary>
    /// An entry may name several repositories, and pushing to one of them says
    /// nothing about the others. This used to replace the whole set with the one
    /// repository the push went to, so a two-target entry silently lost a target
    /// and the person's next look at the row showed one repository where they had
    /// typed two.
    /// </summary>
    [Fact]
    public async Task Pushing_one_target_keeps_the_other()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(["JSdotNet/Backlog", "JSdotNet/Docs"]);
        await store.SaveAsync(entry);

        var result = await new LinkTaskToIssueCommandHandler(store)
            .Handle(new LinkTaskToIssueCommand(entry.Id, "JSdotNet/Docs", "42", "issue"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["JSdotNet/Backlog", "JSdotNet/Docs"], result.Value.RepoIds!);
    }

    /// <summary>A push to a repository the entry did not name adds it rather than
    /// replacing what is there — the desktop push flow lets somebody choose a
    /// target, and choosing one is not a request to clear the rest.</summary>
    [Fact]
    public async Task Pushing_to_a_repository_the_entry_did_not_name_adds_it()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(["JSdotNet/Backlog"]);
        await store.SaveAsync(entry);

        var result = await new LinkTaskToIssueCommandHandler(store)
            .Handle(new LinkTaskToIssueCommand(entry.Id, "JSdotNet/Docs", "42", "issue"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["JSdotNet/Backlog", "JSdotNet/Docs"], result.Value.RepoIds!);
    }

    /// <summary>Two casings are one target, so the stored spelling — which came
    /// from the registry — is the one that stays.</summary>
    [Fact]
    public async Task Pushing_to_a_target_the_entry_already_names_adds_nothing()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(["JSdotNet/Backlog"]);
        await store.SaveAsync(entry);

        var result = await new LinkTaskToIssueCommandHandler(store)
            .Handle(new LinkTaskToIssueCommand(entry.Id, "jsdotnet/backlog", "42", "issue"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["JSdotNet/Backlog"], result.Value.RepoIds!);
    }

    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public List<TaskItem> Entries { get; } = [];

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(Entries);

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == id));

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            Entries.RemoveAll(existing => existing.Id == task.Id);
            Entries.Add(task);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Entries.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }
    }
}
