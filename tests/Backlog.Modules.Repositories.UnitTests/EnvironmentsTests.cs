using Backlog.Modules.Repositories.Abstractions.Environments;
using Backlog.Modules.Repositories.Abstractions.Services;
using Backlog.Modules.Repositories.Extensions;
using Backlog.Modules.Repositories.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Repositories.UnitTests;

/// <summary>
/// A repository's named environments and the no-repository group's
/// (<c>domain/repositories</c>, local ADR 0073 §3), against the invariants the registry
/// keeps for them: one home, a name and one launch link for life, and arranging that
/// leaves the link alone.
/// </summary>
public sealed class EnvironmentsTests
{
    private static readonly EnvironmentHome App = EnvironmentHome.Repository("org/app");
    private static readonly EnvironmentHome Other = EnvironmentHome.Repository("org/other");
    private static readonly LaunchLink Staging = new(LaunchLinkKind.Url, "https://staging.example.com");

    private readonly InMemoryEnvironmentStore _store = new();
    private readonly Environments _environments;

    public EnvironmentsTests() => _environments = new Environments(_store);

    [Fact]
    public void A_repository_lists_the_environments_added_to_it_in_the_order_they_were_added()
    {
        Assert.Null(_environments.Add(App, "Staging", EnvironmentType.Staging, Staging));
        Assert.Null(_environments.Add(App, "Local", EnvironmentType.Local, new LaunchLink(LaunchLinkKind.Command, "aspire start")));
        Assert.Null(_environments.Add(Other, "Production", EnvironmentType.Production, Staging));

        Assert.Equal(["Staging", "Local"], _environments.For(App).Select(e => e.Name));
        Assert.Equal(["Production"], _environments.For(Other).Select(e => e.Name));
    }

    [Fact]
    public void A_repository_id_names_its_home_whatever_its_case()
    {
        _environments.Add(App, "Staging", EnvironmentType.Staging, Staging);

        Assert.Single(_environments.For(EnvironmentHome.Repository("ORG/App")));
    }

    [Fact]
    public void The_no_repository_group_holds_its_own_environments_apart_from_every_repository()
    {
        _environments.Add(EnvironmentHome.NoRepository, "Azure portal", EnvironmentType.Cloud, new LaunchLink(LaunchLinkKind.Url, "https://portal.azure.com"));
        _environments.Add(App, "Staging", EnvironmentType.Staging, Staging);

        Assert.Equal(["Azure portal"], _environments.For(EnvironmentHome.NoRepository).Select(e => e.Name));
        Assert.Equal(["Staging"], _environments.For(App).Select(e => e.Name));
    }

    [Theory]
    [InlineData("", "https://x.example.com", LaunchLinkKind.Url)]
    [InlineData("Staging", "  ", LaunchLinkKind.Url)]
    [InlineData("Staging", "staging.example.com", LaunchLinkKind.Url)]
    [InlineData("Staging", "ftp://staging.example.com", LaunchLinkKind.Url)]
    public void An_environment_needs_a_name_and_a_launch_link_that_can_be_opened(string name, string value, LaunchLinkKind kind)
    {
        var error = _environments.Add(App, name, EnvironmentType.Staging, new LaunchLink(kind, value));

        Assert.NotNull(error);
        Assert.Empty(_environments.For(App, includeHidden: true));
    }

    [Fact]
    public void A_name_and_launch_link_are_kept_trimmed_and_blank_access_hints_dropped()
    {
        _environments.Add(App, "  Staging ", EnvironmentType.Staging, new LaunchLink(LaunchLinkKind.Url, " https://staging.example.com "), ["Key Vault kv-app", " ", "  VPN required "]);

        var environment = Assert.Single(_environments.For(App));
        Assert.Equal("Staging", environment.Name);
        Assert.Equal("https://staging.example.com", environment.Link.Value);
        Assert.Equal([new AccessHint("Key Vault kv-app"), new AccessHint("VPN required")], environment.AccessHints);
    }

    [Fact]
    public void Pinned_environments_come_first_and_keep_their_order_among_themselves()
    {
        _environments.Add(App, "One", EnvironmentType.Local, Staging);
        _environments.Add(App, "Two", EnvironmentType.Local, Staging);
        _environments.Add(App, "Three", EnvironmentType.Local, Staging);

        _environments.SetPinned(App, Id("Three"), true);
        _environments.SetPinned(App, Id("Two"), true);

        Assert.Equal(["Two", "Three", "One"], _environments.For(App).Select(e => e.Name));
    }

    [Fact]
    public void A_hidden_environment_stays_out_of_the_list_until_asked_for()
    {
        _environments.Add(App, "One", EnvironmentType.Local, Staging);
        _environments.Add(App, "Two", EnvironmentType.Local, Staging);

        _environments.SetHidden(App, Id("One"), true);

        Assert.Equal(["Two"], _environments.For(App).Select(e => e.Name));
        Assert.Equal(["One", "Two"], _environments.For(App, includeHidden: true).Select(e => e.Name));
        Assert.Equal(1, _environments.HiddenCount(App));
    }

    [Fact]
    public void Moving_an_environment_changes_its_place_among_its_home_only()
    {
        _environments.Add(App, "One", EnvironmentType.Local, Staging);
        _environments.Add(Other, "Elsewhere", EnvironmentType.Local, Staging);
        _environments.Add(App, "Two", EnvironmentType.Local, Staging);

        Assert.Null(_environments.Move(App, Id("Two"), -1));

        Assert.Equal(["Two", "One"], _environments.For(App).Select(e => e.Name));
        Assert.Equal(["Elsewhere"], _environments.For(Other).Select(e => e.Name));
        Assert.NotNull(_environments.Move(App, Id("Two"), -1));
    }

    [Fact]
    public void Arranging_an_environment_leaves_its_name_type_and_launch_link_alone()
    {
        _environments.Add(App, "One", EnvironmentType.Staging, Staging, ["VPN"]);
        _environments.Add(App, "Two", EnvironmentType.Local, Staging);
        var before = Single("One");

        _environments.SetPinned(App, before.Id, true);
        _environments.SetHidden(App, before.Id, true);
        _environments.Move(App, before.Id, 1);

        var after = Single("One");
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Type, after.Type);
        Assert.Equal(before.Link, after.Link);
        Assert.Equal(before.AccessHints, after.AccessHints);
        Assert.True(after.Pinned);
        Assert.True(after.Hidden);
    }

    [Fact]
    public void An_environment_is_arranged_only_in_its_own_home()
    {
        _environments.Add(App, "One", EnvironmentType.Local, Staging);

        Assert.NotNull(_environments.SetPinned(Other, Id("One"), true));
        Assert.NotNull(_environments.Remove(EnvironmentHome.NoRepository, Id("One")));
        Assert.Single(_environments.For(App));
    }

    [Fact]
    public void Removing_an_environment_takes_it_out_of_its_home()
    {
        _environments.Add(App, "One", EnvironmentType.Local, Staging);
        _environments.Add(App, "Two", EnvironmentType.Local, Staging);

        Assert.Null(_environments.Remove(App, Id("One")));

        Assert.Equal(["Two"], _environments.For(App).Select(e => e.Name));
    }

    [Fact]
    public void Every_edit_is_written_to_the_store_and_announced()
    {
        var announced = 0;
        _environments.Changed += () => announced++;

        _environments.Add(App, "One", EnvironmentType.Local, Staging);
        _environments.SetPinned(App, Id("One"), true);

        Assert.Equal(2, announced);
        Assert.Equal("One", Assert.Single(_store.Load()!).Environment.Name);
        Assert.True(_store.Load()![0].Environment.Pinned);
    }

    [Fact]
    public void A_change_underneath_is_read_back()
    {
        var announced = 0;
        _environments.Changed += () => announced++;

        _store.Replace([new StoredEnvironment(App, new RepositoryEnvironment(Guid.NewGuid(), "Synced", EnvironmentType.Test, Staging, []))]);

        Assert.Equal(1, announced);
        Assert.Equal(["Synced"], _environments.For(App).Select(e => e.Name));
    }

    [Fact]
    public void The_module_registers_environments_over_an_in_memory_store_a_host_can_replace()
    {
        using var provider = new ServiceCollection()
            .AddRepositoriesModule()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var environments = provider.GetRequiredService<IEnvironments>();

        Assert.Null(environments.Add(App, "One", EnvironmentType.Local, Staging));
        Assert.Single(provider.GetRequiredService<IEnvironmentStore>().Load()!);
        Assert.NotNull(provider.GetRequiredService<IEnvironmentCommandRunner>().Run("dir", null));
    }

    [Fact]
    public void A_move_passes_over_hidden_environments_to_the_next_one_shown()
    {
        _environments.Add(App, "One", EnvironmentType.Local, Staging);
        _environments.Add(App, "Secret", EnvironmentType.Local, Staging);
        _environments.Add(App, "Two", EnvironmentType.Local, Staging);
        _environments.SetHidden(App, Id("Secret"), true);

        Assert.Null(_environments.Move(App, Id("Two"), -1));

        Assert.Equal(["Two", "One"], _environments.For(App).Select(e => e.Name));
        Assert.Equal(["Two", "One", "Secret"], _environments.For(App, includeHidden: true).Select(e => e.Name));
    }

    [Fact]
    public void A_move_stays_inside_its_pin_group()
    {
        _environments.Add(App, "Pinned", EnvironmentType.Local, Staging);
        _environments.Add(App, "Loose", EnvironmentType.Local, Staging);
        _environments.SetPinned(App, Id("Pinned"), true);

        Assert.Equal(Environments.CannotMove, _environments.Move(App, Id("Loose"), -1));
        Assert.Equal(Environments.CannotMove, _environments.Move(App, Id("Pinned"), 1));
        Assert.Equal(["Pinned", "Loose"], _environments.For(App).Select(e => e.Name));
    }

    [Fact]
    public void An_edit_whose_save_fails_holds_for_the_session_and_says_so()
    {
        var environments = new Environments(new FailingStore());

        Assert.Equal(IEnvironments.KeptNotSaved, environments.Add(App, "One", EnvironmentType.Local, Staging));
        Assert.Single(environments.For(App));
    }

    [Fact]
    public void A_store_that_cannot_be_read_refuses_every_edit_rather_than_overwrite_it()
    {
        var environments = new Environments(new FailingStore { Unreadable = true });

        Assert.Equal(IEnvironments.StoreUnreadable, environments.Add(App, "One", EnvironmentType.Local, Staging));
        Assert.Empty(environments.For(App));
    }

    [Fact]
    public void A_failed_read_underneath_keeps_what_is_held()
    {
        var store = new FailingStore();
        var environments = new Environments(store);
        environments.Add(App, "One", EnvironmentType.Local, Staging);

        store.Unreadable = true;
        store.Raise();

        Assert.Single(environments.For(App));
    }

    private sealed class FailingStore : IEnvironmentStore
    {
        public bool Unreadable { get; set; }

        public event Action? Changed;

        public IReadOnlyList<StoredEnvironment>? Load() => Unreadable ? null : [];

        public string? Save(IReadOnlyList<StoredEnvironment> environments) => "disk full";

        public void Raise() => Changed?.Invoke();
    }

    private Guid Id(string name) => Single(name).Id;

    private RepositoryEnvironment Single(string name) =>
        _environments.For(App, includeHidden: true).Single(e => e.Name == name);
}
