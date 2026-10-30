using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// <see cref="ISurfaceWindowHost"/> for the web harness (local ADR 0043 §7). A browser
/// has tabs and no native windows, so <c>target: "desktop"</c> opens nothing and says
/// so; a web link is this harness's own <c>/window/{surfaceId}</c> page, at the address
/// it is listening on, with no token, because the harness guards no page with one.
/// </summary>
/// <param name="server">The server whose bound addresses a link names.</param>
internal sealed class HarnessSurfaceWindowHost(IServer server) : ISurfaceWindowHost
{
    /// <inheritdoc />
    public Task<bool> TryOpenWindowAsync(string surfaceId, IReadOnlyDictionary<string, string> query) => Task.FromResult(false);

    /// <inheritdoc />
    public Uri? WebLink(string surfaceId, IReadOnlyDictionary<string, string> query)
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];

        // http before https: the loopback address a browser pane opens without a
        // certificate prompt, when the harness binds both.
        var address = addresses
            .OrderBy(candidate => candidate.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(uri => uri is not null && uri.Port > 0);

        return address is null ? null : new Uri(address, SurfaceLink.Path(surfaceId, query));
    }
}
