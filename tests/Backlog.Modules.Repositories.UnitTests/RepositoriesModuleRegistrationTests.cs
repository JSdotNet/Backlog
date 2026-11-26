using Backlog.Modules.Repositories.Abstractions;
using Backlog.Modules.Repositories.Abstractions.Services;
using Backlog.Modules.Repositories.Extensions;
using Backlog.Modules.Repositories.UnitTests.Fakes;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Backlog.Modules.Repositories.UnitTests.Fakes.Statuses;

namespace Backlog.Modules.Repositories.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddRepositoriesModule()</c>: one
/// <see cref="IPullRequests"/> for the whole process, and — until the host registers the
/// GitHub adapters — fallbacks that answer "not configured" rather than failing to
/// resolve.
/// </summary>
public sealed class RepositoriesModuleRegistrationTests
{
    [Fact]
    public async Task A_host_with_no_adapters_gets_a_contract_that_is_not_configured_and_reads_nothing()
    {
        using var provider = new ServiceCollection()
            .AddRepositoriesModule()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var pullRequests = provider.GetRequiredService<IPullRequests>();
        var listing = await pullRequests.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);
        var read = await pullRequests.ReadAsync([Ref(1)], cancellationToken: TestContext.Current.CancellationToken);
        var merged = await pullRequests.MergeAsync(Ref(1), TestContext.Current.CancellationToken);

        Assert.False(pullRequests.IsConfigured);
        Assert.Empty(listing.PullRequests);
        Assert.Empty(listing.Failures);
        Assert.Empty(read.PullRequests);
        Assert.Equal(PullRequestErrors.NotConfiguredCode, Assert.IsType<Error>(merged.Value).Code);
        Assert.Same(pullRequests, provider.GetRequiredService<IPullRequests>());
    }

    [Fact]
    public async Task The_adapters_a_host_registers_answer_the_contract()
    {
        var github = new CountingPullRequestSource().Holds(Pull(1));
        var services = new ServiceCollection().AddRepositoriesModule();
        services.Replace(ServiceDescriptor.Singleton<IPullRequestSource>(github));
        services.Replace(ServiceDescriptor.Singleton<IRegisteredRepositories>(new FakeRegisteredRepositories(BacklogRepository)));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var pullRequests = provider.GetRequiredService<IPullRequests>();
        var listing = await pullRequests.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(pullRequests.IsConfigured);
        Assert.Equal(1, Assert.Single(listing.PullRequests).Number);
    }
}
