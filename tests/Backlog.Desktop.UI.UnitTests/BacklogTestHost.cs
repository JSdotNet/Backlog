using Backlog.Infrastructure.GitHub;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Modules.Backlog.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Composes the Backlog module the way a host does, so these tests drive the
/// desktop through the real use cases rather than a stand-in for them.
/// <para>
/// The desktop project itself can no longer see the module implementation — it
/// only knows <see cref="IBacklogEntries"/> — but a test is a host, and wiring
/// the handlers to a real file store is exactly what makes these tests worth
/// having.
/// </para>
/// </summary>
internal static class BacklogTestHost
{
    public static IBacklogRepository RepositoryFor(WorkspaceSettingsStore store) =>
        new RootedFileBacklogRepository(() => store.RootDirectory);

    public static IBacklogEntries EntriesFor(WorkspaceSettingsStore store) =>
        EntriesFor(RepositoryFor(store));

    public static IBacklogEntries EntriesFor(IBacklogRepository repository) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .AddBacklogModule()
            .BuildServiceProvider()
            .GetRequiredService<IBacklogEntries>();

    /// <summary>
    /// Backlog Management's store port over a workspace settings file. The two
    /// are composed by a host, and a test is a host: the port answers where the
    /// backlog is from the settings file and where a repository's .backlog folder
    /// is from the same resolver Second Brain uses, which is the join neither
    /// context is allowed to make for itself.
    /// </summary>
    public static IBacklogStore BacklogStoreFor(
        WorkspaceSettingsStore settings,
        IKnowledgeFolderSource? folders = null) =>
        new WorkspaceBacklogStore(
            settings,
            folders ?? new KnowledgeFolderSource(
                new GitHubSettingsStore(Path.Combine(
                    Path.GetTempPath(),
                    "backlog-test-host",
                    Guid.NewGuid().ToString("n"),
                    "github.json")),
                settings));

    public static BacklogDesktopState StateFor(
        WorkspaceSettingsStore store,
        GitHubIntegration gitHub,
        BacklogCopilotCli? copilot = null,
        RepositoryBacklogSource? repositoryBacklog = null,
        IKnowledgeFolderSource? folders = null) =>
        new(BacklogStoreFor(store, folders), EntriesFor(store), gitHub, copilot, repositoryBacklog);
}
