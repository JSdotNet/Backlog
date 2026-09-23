extern alias DesktopHarness;

using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// That a host which registers the MCP server also composes everything its tools
/// are built from.
/// <para>
/// This is the failure mode <c>TaskSyncClientRegistrationTests</c> was written
/// for, one context along: a registration that builds cleanly and only dies later
/// inside provider validation, or — worse here — not until a session calls the
/// tool. The SDK constructs each <c>[McpServerToolType]</c> class <em>whole</em>
/// with <c>ActivatorUtilities.CreateInstance(request.Services, …)</c> for every
/// invocation, so one missing port is not one broken tool: it is every tool on
/// that class. A head that composed no <c>IRoadmapPlanning</c> would break the
/// roadmap tool and any tool sharing its class, and would do it at the first call
/// rather than at startup.
/// </para>
/// <para>
/// Asserted by constructing the tool classes the way the SDK does, rather than by
/// listing the ports here. A list would be a copy of the tool classes'
/// constructors kept in a test project, and a copy is what drifts — a tool that
/// grows a dependency has to fail this test, not quietly outgrow it.
/// </para>
/// <para>
/// The desktop web harness is the host this can be done to. The MAUI head cannot
/// be composed in a test process at all, so what covers it is
/// <see cref="McpServerRegistrationTests"/>, a source scan, on the same terms and
/// with the same limits as the two sync rules beside it.
/// </para>
/// </summary>
public class McpServerCompositionTests
{
    /// <summary>Development on purpose, for the reason
    /// <see cref="WebHarnessHostTests"/> gives: the generic host only turns
    /// <c>ValidateOnBuild</c> and <c>ValidateScopes</c> on there, and that
    /// validation is what turns an unsatisfiable registration into a startup
    /// failure. Every AppHost run of this harness is a Development run.</summary>
    private sealed class Harness<TEntryPoint> : WebApplicationFactory<TEntryPoint>
        where TEntryPoint : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Every tool class the library publishes can be constructed out of a request
    /// scope of the harness's own container — which is exactly what the SDK does
    /// per invocation.
    /// <para>
    /// From a scope and not from the root, because that is where it happens:
    /// <c>McpServerOptions.ScopeRequests</c> is left at its default <c>true</c>
    /// so that <c>request.Services</c> is a per-request scope, and
    /// <c>ITaskItems</c> and <c>IRoadmapPlanning</c> are registered
    /// <c>AddScoped</c>. Resolving them from the root provider would throw here
    /// under scope validation, which is the point.
    /// </para>
    /// </summary>
    [Fact]
    public void The_desktop_harness_can_construct_every_mcp_tool_class()
    {
        using var harness = new Harness<DesktopHarness::Program>();
        using var scope = harness.Services.CreateScope();

        // A guard against the whole test passing because the group table is
        // empty: no groups would make every Assert.All below vacuous.
        Assert.NotEmpty(BacklogMcpTools.Groups);
        Assert.NotEmpty(BacklogMcpTools.ToolNames);

        Assert.All(
            BacklogMcpTools.Groups,
            group => Assert.NotNull(
                ActivatorUtilities.CreateInstance(scope.ServiceProvider, group.ToolType)));
    }

    /// <summary>
    /// The feature switches resolve from a request scope too.
    /// <para>
    /// A separate assertion from the one above because it is a separate path:
    /// no tool class takes <see cref="IAppFeatureSettings"/> in its constructor,
    /// so the test above would pass without it. It is the two request filters
    /// that ask — the list-tools filter to drop a switched-off group and the
    /// call-tool filter to refuse a call to one — and they ask
    /// <c>request.Services</c> per request. A host that composed none would list
    /// every tool and refuse none, with nothing failing to say so.
    /// </para>
    /// </summary>
    [Fact]
    public void The_desktop_harness_composes_the_feature_switches_the_filters_read()
    {
        using var harness = new Harness<DesktopHarness::Program>();
        using var scope = harness.Services.CreateScope();

        var features = scope.ServiceProvider.GetRequiredService<IAppFeatureSettings>();

        Assert.NotNull(features);

        // And every key the groups gate on is one the catalog defines.
        // IAppFeatureSettings.IsEnabled throws for a key no catalog knows — "an
        // unknown feature is a typo in code, not a choice somebody made" — so a
        // group naming a key that was renamed or never added would take down
        // every tools/list rather than hiding one group.
        //
        // Guarded, because Assert.All over an empty collection asserts nothing at
        // all and the group table is a static this test does not build.
        Assert.NotEmpty(BacklogMcpTools.Groups);

        Assert.All(
            BacklogMcpTools.Groups,
            group =>
            {
                Assert.False(string.IsNullOrWhiteSpace(group.FeatureKey));

                // The call is the claim, and the discard is deliberate: what is
                // being asserted is that asking does not throw, not which answer
                // comes back — the answer is whatever this machine's settings
                // file says, and a test that demanded one would fail for whoever
                // had switched the roadmap off. Written as a statement so it
                // cannot be read as an assertion whose result was dropped, which
                // is what `group => features.IsEnabled(group.FeatureKey)` was:
                // Assert.All binds an Action<T> and discards a bool silently.
                _ = features.IsEnabled(group.FeatureKey);
            });
    }

    /// <summary>
    /// Every tool the library publishes answers the exposure predicate without
    /// throwing, for the container the harness actually composed.
    /// <para>
    /// <see cref="BacklogMcpTools.IsExposed"/> is the one call both filters make
    /// per request, and it reads a feature key. This is the cheapest place to
    /// find out that one of those keys is not in the catalog, because in
    /// production the finding arrives as a failed <c>tools/list</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_published_tool_name_can_be_asked_about()
    {
        using var harness = new Harness<DesktopHarness::Program>();
        using var scope = harness.Services.CreateScope();

        var features = scope.ServiceProvider.GetRequiredService<IAppFeatureSettings>();

        Assert.NotEmpty(BacklogMcpTools.ToolNames);

        // Asserted against the group's own switch rather than against true. The
        // answer for a published name is whatever this machine's settings file
        // says — demanding true would fail for whoever had turned the roadmap off
        // — and `name => BacklogMcpTools.IsExposed(name, features)`, which is what
        // stood here, asserted nothing either way: Assert.All binds an Action<T>
        // and a lambda whose body is a bool expression has its answer discarded.
        Assert.All(
            BacklogMcpTools.ToolNames,
            name => Assert.Equal(
                features.IsEnabled(GroupOf(name).FeatureKey),
                BacklogMcpTools.IsExposed(name, features)));

        // A name the library does not publish is left alone rather than hidden,
        // so a host mapping other tools onto the same server keeps them.
        Assert.True(BacklogMcpTools.IsExposed("something_else_entirely", features));
    }

    /// <summary>The group a published tool name belongs to. Asserted rather than
    /// defaulted: a name in <see cref="BacklogMcpTools.ToolNames"/> that belongs
    /// to no group would mean the table had been taken apart underneath the
    /// test.</summary>
    private static BacklogMcpTools.McpToolGroup GroupOf(string toolName)
    {
        var group = BacklogMcpTools.Groups.SingleOrDefault(candidate =>
            candidate.ToolNames.Contains(toolName, StringComparer.Ordinal));

        Assert.NotNull(group);

        return group;
    }
}
