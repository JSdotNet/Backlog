using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Backlog.Desktop.UI.Extensions;

/// <summary>
/// The MCP server both hosts register: the tool classes
/// <see cref="BacklogMcpTools"/> publishes, and the two filters that keep a tool
/// whose feature is switched off out of reach.
/// <para>
/// <b>Why it is here.</b> It has to be somewhere both hosts can call, and there
/// are exactly two: <c>src/App/Backlog.Desktop</c> (the MAUI head) and
/// <c>src/Harness/Backlog.Desktop.WebHarness</c>. The harness cannot reference
/// the MAUI head — it is a <c>WinExe</c> on a platform TFM — so the method
/// cannot live there, and writing it twice is what local ADR 0012 §2 rules out
/// when it says the two hosts keep "the tools one implementation with two
/// hosts". This project is the only one both of them already reference that
/// nothing on the <c>net10.0-android</c> path does: the mobile head reaches
/// <c>ServiceDefaults</c>, <c>Backlog.Mobile.UI</c>,
/// <c>Backlog.Infrastructure.Sync</c> and <c>Backlog.Modules.Tasks</c>, and
/// none of those reaches back here. It is also where
/// <c>AppFeatures.McpServer</c> is declared, so the server's wiring and the key
/// that switches it on stay in one project.
/// </para>
/// <para>
/// <b>Why that is safe.</b> The package this file needs is
/// <c>ModelContextProtocol</c> — the dependency-injection half of the SDK, whose
/// nuspec declares <em>no</em> framework reference. <c>Microsoft.AspNetCore.App</c>
/// arrives only with <c>ModelContextProtocol.AspNetCore</c>, which carries
/// <c>MapMcp</c> and <c>WithHttpTransport</c> and is referenced by the two hosts
/// and by nothing else — so §2's rule that the framework reference lives in
/// <c>Backlog.Desktop.csproj</c> and nowhere else still holds literally.
/// </para>
/// <para>
/// <b>The better home is <c>Backlog.Infrastructure.Mcp</c></b>, which already
/// gives the same argument for holding <see cref="BacklogMcpTools"/> — "both
/// hosts need it and neither owns it" — and which is a leaf nothing mobile
/// references either. Taking <c>ModelContextProtocol</c> there would not put a
/// framework reference on it. This file sits here instead because that project
/// was being changed in parallel when this was written; moving it is a file move
/// and one csproj line.
/// </para>
/// </summary>
public static class BacklogMcpServerRegistration
{
    /// <summary>
    /// Where the endpoint is served from, under whichever address the host
    /// listens on.
    /// <para>
    /// Shared by the two hosts rather than written twice, and that is the whole
    /// of the reason: it has to match the path in the repository's
    /// <c>.mcp.json</c>, and a harness that answered on a different path from
    /// the installed app would be a harness that proved nothing about the app.
    /// Local ADR 0012 §3 wants one server id in every registration so a skill
    /// can name a tool without knowing which registration is in force; one path
    /// is the same requirement one level down.
    /// </para>
    /// </summary>
    public const string EndpointPath = "/mcp";

    /// <summary>
    /// Registers the Backlog MCP server and its tools, without a transport.
    /// <para>
    /// The transport is the caller's: the desktop head adds
    /// <c>WithHttpTransport()</c> on a Kestrel listener of its own, the harness
    /// adds it on the pipeline it already has. Everything above the transport —
    /// which tools exist, and which of them a given request may see — is the
    /// same in both, which is the whole reason this method exists.
    /// </para>
    /// <para>
    /// <see cref="McpServerOptions.ScopeRequests"/> is deliberately left at its
    /// default of <c>true</c>, and that default is load-bearing rather than
    /// incidental: it is what makes <c>request.Services</c> a per-request scope.
    /// <c>ITaskItems</c> and <c>IRoadmapPlanning</c> are registered
    /// <c>AddScoped</c>, so with it off every tool would resolve them from the
    /// root provider — a captive dependency in a process that also runs the
    /// panes off the same container.
    /// </para>
    /// <para>
    /// <b>The transport both hosts add is stateless</b>, which is the SDK's own
    /// default from 2.2.0 — SEP-2567 took <c>Mcp-Session-Id</c> out of the
    /// <c>2026-07-28</c> revision and <c>HttpServerTransportOptions.Stateless</c>
    /// followed it. So there is no long-lived session behind a client: every
    /// request builds a fresh server context over a fresh request scope, which is
    /// the arrangement the tools want anyway. Nothing here relies on it — the
    /// tools make no server-to-client request, and none of sampling, elicitation
    /// or roots is used — and
    /// <c>McpEndpointGateTests.Two_tool_calls_in_a_row_both_answer</c> holds the
    /// property that matters either way: a second call resolves out of a live
    /// scope rather than the first call's disposed one.
    /// </para>
    /// </summary>
    public static IMcpServerBuilder AddBacklogMcpServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddMcpServer()
            // Read off the group table rather than named one by one, so a group
            // added or split there arrives here without this file being touched.
            // Distinct because the table maps keys to types and nothing stops two
            // groups naming one class; registering a class twice would list its
            // tools twice.
            .WithTools([.. BacklogMcpTools.Groups.Select(group => group.ToolType).Distinct()])
            .WithRequestFilters(ApplyFeatureGates);
    }

    /// <summary>
    /// The two halves of local ADR 0012 §7, which are one rule and have to be
    /// installed together.
    /// <para>
    /// The list filter is what makes a switched-off group <em>absent</em> from
    /// <c>tools/list</c> rather than present and refusing, which is what
    /// <c>08-crosscutting-concepts.md#feature-enablement</c> asks of every
    /// switchable capability. The call filter is what makes that true: a client
    /// caches the list it was given, so one taken at 9am still names a tool
    /// switched off at 10, and a tool that is missing from the list but answers
    /// the call anyway is not absent — it is merely undocumented.
    /// </para>
    /// <para>
    /// Both read the flags per request rather than at startup, which is what lets
    /// a person switch a feature and have the next <c>tools/list</c> say so
    /// without restarting the app or the session.
    /// </para>
    /// </summary>
    private static void ApplyFeatureGates(IMcpRequestFilterBuilder filters)
    {
        filters.AddListToolsFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken).ConfigureAwait(false);
            var features = FeaturesFor(request);

            result.Tools = [.. result.Tools.Where(tool => BacklogMcpTools.IsExposed(tool.Name, features))];

            return result;
        });

        filters.AddCallToolFilter(next => async (request, cancellationToken) =>
        {
            var name = request.Params?.Name;

            // IsCallable and not IsExposed: a name this library does not publish
            // is still left alone — a host may map other tools onto the same
            // server and this filter is not theirs — but one of ours is
            // recognised whatever case it arrives in, so no dispatcher matching
            // more loosely than the list filter does could route list_ENTRIES to
            // a switched-off group.
            if (name is not null && !BacklogMcpTools.IsCallable(name, FeaturesFor(request)))
            {
                // Worded as the SDK words a name it has never heard of, and
                // deliberately so: to the caller a switched-off tool and a
                // misspelt one are the same thing, and saying "that feature is
                // off" would describe a capability the answer is meant to be
                // denying the existence of.
                throw new McpException($"Unknown tool: '{name}'");
            }

            return await next(request, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>The feature switches for this request, out of the request's own
    /// scope. Resolved per call rather than captured when the filter was built,
    /// because the filter is built once for the process and the answer has to be
    /// current — and because the scope is where a scoped collaborator of the
    /// settings adapter would have to come from.</summary>
    private static IAppFeatureSettings FeaturesFor<TParams>(RequestContext<TParams> request) =>
        request.Services is { } scope
            ? scope.GetRequiredService<IAppFeatureSettings>()
            : throw new InvalidOperationException(
                "The MCP request carries no service provider, so the feature switches cannot be read. "
                + "That means McpServerOptions.ScopeRequests was turned off; leave it at its default.");
}
