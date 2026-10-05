using Backlog.Modules.Sync.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The repository registry and the GitHub accounts are not tasks (local ADR 0021,
/// Decision §4). Each goes to the GitHub settings port whole — its text and its
/// stamp — never to the task store, and how it is counted follows what the port
/// said.
/// </summary>
public sealed class TaskReplicaMergeGitHubTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private const string RegistryJson = """{"repositories":[{"id":"innovadis-dev/spec-manager","alias":"spec-manager","account":"j-schepers_innobv"}]}""";
    private const string AccountsJson = """{"updatedAt":"2026-10-05T09:00:00+00:00","accounts":[{"login":"j-schepers_innobv","displayName":"Work"}]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_documents_go_to_the_github_settings_whole_and_never_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var github = new RecordingGitHubSettingsReplication();

        var merged = await new TaskReplicaMerge(tasks, github: github).ApplyAsync(
            [
                TaskChanges.Record(GitHubReplicaChanges.Registry(RegistryJson, Morning), OtherPc, 100),
                TaskChanges.Record(GitHubReplicaChanges.Accounts(AccountsJson, Morning.AddMinutes(1)), OtherPc, 101),
            ],
            Cancellation);

        Assert.Equal(new TaskMergeOutcome(2, 0), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);

        Assert.Collection(
            github.Offered.OrderBy(offer => offer.Document),
            registry =>
            {
                Assert.Equal(GitHubReplicaDocument.Registry, registry.Document);
                Assert.Equal(RegistryJson, registry.Copy.Content);
                Assert.Equal(Morning, registry.Copy.UpdatedAt);
            },
            accounts =>
            {
                Assert.Equal(GitHubReplicaDocument.Accounts, accounts.Document);
                Assert.Equal(AccountsJson, accounts.Copy.Content);
                Assert.Equal(Morning.AddMinutes(1), accounts.Copy.UpdatedAt);
            });
    }

    /// <summary>ADR 0021 Verification 8: an older copy and an echo write nothing, so
    /// they count as neither applied nor skipped.</summary>
    [Theory]
    [InlineData(GitHubReplicaOutcome.Echo)]
    [InlineData(GitHubReplicaOutcome.Refused)]
    public async Task An_echo_or_an_older_copy_counts_as_nothing_applied(GitHubReplicaOutcome answer)
    {
        var github = new RecordingGitHubSettingsReplication { Answer = answer };
        var activity = new SyncActivityLog();

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, github: github)
            .ApplyAsync([TaskChanges.Record(GitHubReplicaChanges.Registry(RegistryJson, Morning), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(activity.Snapshot());
    }

    /// <summary>ADR 0021 Verification 9: a payload that does not parse counts
    /// Skipped, and the port has left the local accounts as they were.</summary>
    [Fact]
    public async Task A_copy_the_store_cannot_read_counts_skipped()
    {
        var github = new RecordingGitHubSettingsReplication { Answer = GitHubReplicaOutcome.Unreadable };

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), github: github)
            .ApplyAsync([TaskChanges.Record(GitHubReplicaChanges.Accounts("not accounts", Morning), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
    }

    [Fact]
    public async Task A_taken_copy_is_recorded_as_a_github_settings_document_received()
    {
        var activity = new SyncActivityLog();

        await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, github: new RecordingGitHubSettingsReplication())
            .ApplyAsync([TaskChanges.Record(GitHubReplicaChanges.Accounts(AccountsJson, Morning), OtherPc, 100)], Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(SyncItemKind.GitHubSettings, entry.Kind);
        Assert.Equal(GitHubReplicaDocuments.IdentitiesId.ToString("D"), entry.Id);
        Assert.Equal("GitHub accounts", entry.Title);
    }

    /// <summary>A merge without the port — what an older build, or the phone, is —
    /// counts both kinds Skipped and writes no task.</summary>
    [Fact]
    public async Task Without_the_port_both_kinds_are_skipped_and_no_task_is_written()
    {
        var tasks = new InMemoryTaskStore();

        var merged = await new TaskReplicaMerge(tasks).ApplyAsync(
            [
                TaskChanges.Record(GitHubReplicaChanges.Registry(RegistryJson, Morning), OtherPc, 100),
                TaskChanges.Record(GitHubReplicaChanges.Accounts(AccountsJson, Morning), OtherPc, 101),
            ],
            Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 2), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);
    }

    [Fact]
    public async Task A_task_and_a_roadmap_document_in_the_same_page_still_land_where_they_belong()
    {
        var tasks = new InMemoryTaskStore();
        var roadmap = new RecordingRoadmapReplication();
        var github = new RecordingGitHubSettingsReplication();
        var task = TaskChanges.Change("Ordinary work", Morning);

        var merged = await new TaskReplicaMerge(tasks, roadmap: roadmap, github: github).ApplyAsync(
            [
                TaskChanges.Record(GitHubReplicaChanges.Registry(RegistryJson, Morning), OtherPc, 100),
                TaskChanges.Record(RoadmapReplicaChanges.Plan("""{"version":1}""", Morning), OtherPc, 101),
                TaskChanges.Record(task, OtherPc, 102),
            ],
            Cancellation);

        Assert.Equal(3, merged.Applied);
        Assert.Equal([task.Id], tasks.Writes);
        Assert.Single(roadmap.Offered);
        Assert.Single(github.Offered);
    }

    /// <summary>The wire shape of ADR 0018 Decision §1, which ADR 0021 reuses: one
    /// constant id, the stamp, the kind token, the text verbatim, a title, the Tasks
    /// defaults, and never a tombstone.</summary>
    [Fact]
    public void A_github_settings_document_is_written_as_a_task_shaped_change()
    {
        var registry = GitHubReplicaChanges.Registry(RegistryJson, Morning);
        var accounts = GitHubReplicaChanges.Accounts(AccountsJson, Morning);

        Assert.Equal(GitHubReplicaDocuments.RegistryId, registry.Id);
        Assert.Equal(GitHubReplicaDocuments.IdentitiesId, accounts.Id);
        Assert.NotEqual(registry.Id, accounts.Id);
        Assert.Equal(Morning, registry.UpdatedAt);
        Assert.Null(registry.DeletedAt);
        Assert.Equal("repository-registry", registry.Task.Type);
        Assert.Equal("github-accounts", accounts.Task.Type);
        Assert.Equal(RegistryJson, registry.Task.ContentMd);
        Assert.Equal("Repository registry", registry.Task.Title);
        Assert.Equal("GitHub accounts", accounts.Task.Title);
        Assert.Equal("draft", registry.Task.Status);
        Assert.Equal("medium", registry.Task.Priority);
        Assert.Empty(registry.Task.Tags);
    }

    /// <summary>The ids are written on every device and a changed one forks the
    /// document, so the literals are pinned here.</summary>
    [Fact]
    public void The_document_ids_never_change()
    {
        Assert.Equal(Guid.Parse("42925550-7da3-413f-ba66-d3584ef64d27"), GitHubReplicaDocuments.RegistryId);
        Assert.Equal(Guid.Parse("1be600c0-ac5f-47cb-9e92-4f34fa9c2ef2"), GitHubReplicaDocuments.IdentitiesId);
        Assert.NotEqual(RoadmapReplicaDocuments.PlanId, GitHubReplicaDocuments.RegistryId);
        Assert.NotEqual(RoadmapReplicaDocuments.PaceId, GitHubReplicaDocuments.IdentitiesId);
    }
}
