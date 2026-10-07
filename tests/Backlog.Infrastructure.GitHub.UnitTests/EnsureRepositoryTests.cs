using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// <see cref="GitHubSettingsStore.EnsureRepository"/>: making sure one repository is
/// configured without restating the whole list.
/// <para>
/// The web harness seeds its own checkout on every start, into a registry that is
/// the workspace's shared <c>repos.json</c> and travels to every paired device
/// (local ADR 0021). The seed used to be <c>SetRepositories</c> with a list of one,
/// which replaced every other repository and its account binding, and a follow-up
/// loop that reset the folder choices of the one it kept. These tests hold the
/// upsert to the opposite contract: the other rows are not its business, and an
/// existing row gives up nothing but its clone directory.
/// </para>
/// </summary>
public sealed class EnsureRepositoryTests : IDisposable
{
    private const string Work = "j-schepers_innobv";

    private static readonly DateTimeOffset Morning = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ensure-repository-tests-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private GitHubSettingsStore Store(TimeProvider? clock = null) =>
        new(Path.Combine(_root, "local", "github.json"), () => WorkspaceRoot, clock);

    private string Clone(string name = "clone") => Path.Combine(_root, name);

    /// <summary>The seed exactly as the harness builds it.</summary>
    private GitHubRepositoryRef Seed(string? cloneDirectory = null) =>
        new("backlog", "JSdotNet", "Backlog")
        {
            CloneDirectory = cloneDirectory ?? Clone(),
            DevbookFolders = DevbookFolderSetting.Defaults()
        };

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The defect itself: a start that seeds the checkout must leave every other
    /// configured repository exactly as somebody configured it — alias, hue and the
    /// account it is worked as — and must not record any of them as removed, which
    /// would travel to the other devices as a removal nobody made.
    /// </summary>
    [Fact]
    public void Ensuring_keeps_every_other_repository_with_its_binding_colour_and_alias()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager")]));
        Assert.Null(store.SetRepositoryAccount("spec", Work));
        Assert.Null(store.SetRepositoryColour("spec", 4));

        Assert.Null(store.EnsureRepository(Seed()));

        // Read through a restart, so this is the file and not the list in memory.
        var reopened = Store();
        var spec = reopened.Current.Find("innovadis-dev/spec-manager");
        Assert.NotNull(spec);
        Assert.Equal("spec", spec.Alias);
        Assert.Equal(Work, spec.Account);
        Assert.Equal(4, spec.Colour);
        Assert.NotNull(reopened.Current.Find("JSdotNet/Backlog"));
        Assert.Empty(reopened.Current.Removals);
    }

    [Fact]
    public void Ensuring_an_absent_repository_adds_it_once()
    {
        var store = Store();

        Assert.Null(store.EnsureRepository(Seed()));
        Assert.Null(store.EnsureRepository(Seed()));
        Assert.Null(store.EnsureRepository(Seed() with { Alias = "other-alias" }));

        var row = Assert.Single(Store().Current.Repositories);
        Assert.Equal("JSdotNet/Backlog", row.FullName);
        Assert.Equal("backlog", row.Alias);
        Assert.Equal(Clone(), row.CloneDirectory);
    }

    /// <summary>
    /// The alias the seed asks for may already be somebody else's: a different
    /// repository called <c>backlog</c>. That row is theirs and keeps its alias; the
    /// seed takes the compound <c>owner-name</c> form instead, the way a plan import
    /// registering beside a namesake does, so no two rows ever share an alias.
    /// </summary>
    [Fact]
    public void An_alias_another_repository_holds_is_left_to_it_and_the_seed_takes_a_free_one()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "x", "backlog") { CloneDirectory = Clone("theirs") }]));

        Assert.Null(store.EnsureRepository(Seed()));

        var reopened = Store();
        var theirs = reopened.Current.Find("x/backlog");
        Assert.NotNull(theirs);
        Assert.Equal("backlog", theirs.Alias);
        Assert.Equal(Clone("theirs"), theirs.CloneDirectory);

        var seeded = reopened.Current.Find("JSdotNet/Backlog");
        Assert.NotNull(seeded);
        Assert.Equal("jsdotnet-backlog", seeded.Alias);
        Assert.Equal(2, reopened.Current.Repositories.Select(r => r.Alias).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The clone directory is this machine's half, so moving it is no new version
    /// of the shared registry: nothing is sent to the paired devices for it.
    /// </summary>
    [Fact]
    public async Task Moving_the_clone_directory_does_not_restamp_the_shared_registry()
    {
        var clock = new FakeTimeProvider(Morning);
        var store = Store(clock);
        var replication = new GitHubSettingsReplication(store);
        Assert.Null(store.EnsureRepository(Seed(Clone("old"))));
        var stamped = (await replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt;

        clock.Advance(TimeSpan.FromHours(1));
        var shared = 0;
        replication.Changed += () => shared++;

        Assert.Null(store.EnsureRepository(Seed(Clone("new"))));

        Assert.Equal(Clone("new"), store.Current.Find("JSdotNet/Backlog")!.CloneDirectory);
        Assert.Equal(0, shared);
        Assert.Equal(stamped, (await replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);
    }

    /// <summary>
    /// A row that is already there keeps everything decided about it. Only the clone
    /// directory follows the seed, because that is the one thing the seed knows
    /// better than the row: which checkout this harness is running from.
    /// </summary>
    [Fact]
    public void An_existing_row_keeps_its_choices_and_only_its_clone_directory_moves()
    {
        var store = Store();
        Assert.Null(store.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("bl", "jsdotnet", "backlog") { CloneDirectory = Clone("old") }]));
        Assert.Null(store.SetRepositoryAccount("bl", Work));
        Assert.Null(store.SetRepositoryColour("bl", 2));
        Assert.Null(store.SetDevbookSource("bl", "release", useLocalFolder: true));
        Assert.Null(store.SetDevbookFolder("bl", ".domain", enabled: false, path: "docs/domain"));

        Assert.Null(store.EnsureRepository(Seed(Clone("new"))));

        var row = Assert.Single(Store().Current.Repositories);
        Assert.Equal("bl", row.Alias);
        Assert.Equal(Work, row.Account);
        Assert.Equal(2, row.Colour);
        Assert.Equal("release", row.DevbookBranch);
        Assert.True(row.UseLocalDevbookFolder);
        var domain = row.DevbookFolders.Single(f => f.Key == ".domain");
        Assert.False(domain.Enabled);
        Assert.Equal("docs/domain", domain.Path);
        Assert.Equal(Clone("new"), row.CloneDirectory);
    }

    /// <summary>
    /// A start that finds the checkout already seeded writes nothing: no new version
    /// of the registry, which a paired device would take as a change, and no
    /// <see cref="GitHubSettingsStore.Changed"/>, which every open screen reloads on.
    /// </summary>
    [Fact]
    public async Task An_unchanged_ensure_does_not_restamp_the_registry_or_announce_a_change()
    {
        var clock = new FakeTimeProvider(Morning);
        var store = Store(clock);
        var replication = new GitHubSettingsReplication(store);
        Assert.Null(store.EnsureRepository(Seed()));
        var stamped = (await replication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt;

        clock.Advance(TimeSpan.FromHours(1));
        var restarted = Store(clock);
        var restartedReplication = new GitHubSettingsReplication(restarted);
        var announced = 0;
        restarted.Changed += () => announced++;

        Assert.Null(restarted.EnsureRepository(Seed()));

        Assert.Equal(0, announced);
        Assert.Equal(stamped, (await restartedReplication.ReadAsync(GitHubReplicaDocument.Registry, Cancellation))!.UpdatedAt);
    }
}
