using System.Reflection;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Assemblies the router searches for pages besides this one.
/// <para>
/// Registered by a host that carries routes of its own — the desktop harness,
/// for the sample page that throws on request — and by nothing else. A service
/// rather than a parameter on <c>Routes</c> because <c>Routes</c> is an
/// interactive root component in the harness, and a root component's parameters
/// cross to the browser as JSON; an <see cref="Assembly"/> does not serialize,
/// and the page that tried to pass one was a 500 before it rendered anything.
/// </para>
/// </summary>
public sealed record AdditionalRouteAssemblies(IReadOnlyList<Assembly> Assemblies);
