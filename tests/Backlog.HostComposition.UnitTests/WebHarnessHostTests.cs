extern alias DesktopHarness;
extern alias MobileHarness;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// Each browser harness, started the way the AppHost starts it.
/// <para>
/// The assertion is that the host comes up at all. That sounds like nothing to
/// assert until it fails: a service registered for every caller but
/// constructable only in some of them takes the host down inside
/// <c>WebApplicationBuilder.Build()</c>, before a request, a page or a log line —
/// the mobile harness died with exit code -532462766 and a green build and a
/// green unit suite either side of it. Provider validation is what says so, and
/// only a real host turns it on.
/// </para>
/// <para>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> rather than a service
/// collection assembled here, because a collection assembled here is a copy of
/// the host's composition and a copy is what drifts. These run the harness's own
/// <c>Program</c>.
/// </para>
/// </summary>
public class WebHarnessHostTests
{
    /// <summary>
    /// Development on purpose, and it is the whole point of these tests: the
    /// generic host only turns <see cref="ServiceProviderOptions.ValidateOnBuild"/>
    /// and <c>ValidateScopes</c> on in Development, and that validation is what
    /// turns an unsatisfiable registration into a startup failure. Every AppHost
    /// run of these harnesses is a Development run, so this is also what they
    /// actually do.
    /// </summary>
    private sealed class Harness<TEntryPoint> : WebApplicationFactory<TEntryPoint>
        where TEntryPoint : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Development");
    }

    /// <summary>
    /// The desktop harness, which has an <c>ITaskRepository</c> and opts into
    /// task replication. Resolving the session is the second half of the
    /// assertion: a host that opted in and left out its sync-state store would
    /// build and then hide the sync section at runtime, which is the quiet
    /// failure the loud one used to mask.
    /// </summary>
    [Fact]
    public void The_desktop_harness_host_starts()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Backlog.Infrastructure.Sync.TaskSyncSession>());
    }

    /// <summary>
    /// The mobile harness, which has no <c>ITaskRepository</c> and never will:
    /// the phone carries the Inbox, not a local task database. It composes the
    /// pairing surface and nothing of replication, and it has to start.
    /// </summary>
    [Fact]
    public void The_mobile_harness_host_starts()
    {
        using var harness = new Harness<MobileHarness::Program>();

        using var scope = harness.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Backlog.Infrastructure.Sync.DevicePairingClient>());
        Assert.Null(scope.ServiceProvider.GetService<Backlog.Infrastructure.Sync.TaskSyncSession>());
    }
}
