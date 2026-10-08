using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.UnlinkTaskFromIssue;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Forgetting that an entry became something outside this system — the counterpart
/// of <c>LinkTaskToIssue</c>, which is how a session or pull request linked by
/// mistake is taken off its entry again.
/// </summary>
public class UnlinkTaskFromIssueTests
{
    [Fact]
    public async Task Unlinking_removes_only_the_named_link()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(["JSdotNet/Backlog"]);
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "abc", "session"));
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "655", "pull-request"));
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "656", "pull-request"));
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await new UnlinkTaskFromIssueCommandHandler(store)
            .Handle(new UnlinkTaskFromIssueCommand(entry.Id, "JSdotNet/Backlog", "655", "pull-request"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [("abc", "session"), ("656", "pull-request")],
            result.Value.Projections.Select(p => (p.ExternalId, p.TargetType)));
    }

    /// <summary>A session's id and a repository are compared without regard to case,
    /// the way linking decides a session is already linked.</summary>
    [Fact]
    public async Task Unlinking_ignores_case_and_keeps_the_entry_repositories()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(["JSdotNet/Backlog"]);
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "abc", "session"));
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await new UnlinkTaskFromIssueCommandHandler(store)
            .Handle(new UnlinkTaskFromIssueCommand(entry.Id, "jsdotnet/backlog", "ABC", "session"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Projections);
        Assert.Equal(["JSdotNet/Backlog"], result.Value.RepoIds!);
    }

    /// <summary>A pull request numbered the same in another repository is another
    /// pull request.</summary>
    [Fact]
    public async Task Unlinking_a_pull_request_in_another_repository_leaves_this_one()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "655", "pull-request"));
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await new UnlinkTaskFromIssueCommandHandler(store)
            .Handle(new UnlinkTaskFromIssueCommand(entry.Id, "JSdotNet/Docs", "655", "pull-request"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Projections);
    }

    [Fact]
    public async Task Unlinking_from_an_entry_that_is_gone_is_not_found()
    {
        var result = await new UnlinkTaskFromIssueCommandHandler(new InMemoryTaskRepository())
            .Handle(new UnlinkTaskFromIssueCommand(Guid.NewGuid(), "JSdotNet/Backlog", "655", "pull-request"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnlinkTaskFromIssueCommandHandler.NotFound, result.Error);
    }

    /// <summary>A session is one link whatever repository it was recorded under, the
    /// way linking decides it is already linked, so unlinking it takes every ref.</summary>
    [Fact]
    public async Task Unlinking_a_session_takes_it_off_under_every_repository()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "abc", "session"));
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Docs", " abc ", "session"));
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await new UnlinkTaskFromIssueCommandHandler(store)
            .Handle(new UnlinkTaskFromIssueCommand(entry.Id, "JSdotNet/Backlog", "abc", "session"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Projections);
    }

    /// <summary>Nothing to remove is not an edit: the entry keeps its stamp, so an
    /// idle unlink cannot win a sync over a real edit made elsewhere.</summary>
    [Fact]
    public async Task Unlinking_what_the_entry_does_not_hold_leaves_its_stamp()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "655", "pull-request"));
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        var before = (await store.GetAsync(entry.Id, TestContext.Current.CancellationToken))!.UpdatedAt;

        var result = await new UnlinkTaskFromIssueCommandHandler(store)
            .Handle(new UnlinkTaskFromIssueCommand(entry.Id, "JSdotNet/Backlog", "999", "pull-request"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Projections);
        Assert.Equal(before, (await store.GetAsync(entry.Id, TestContext.Current.CancellationToken))!.UpdatedAt);
    }
}
