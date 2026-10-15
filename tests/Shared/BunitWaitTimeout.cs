using System.Runtime.CompilerServices;

using Bunit;

namespace Backlog.Tests;

/// <summary>
/// How long a bUnit <c>WaitFor*</c> keeps asking when the call names no timeout
/// of its own: ten seconds here, against bUnit's one.
///
/// <para>Under the full suite the test modules run in parallel, CI adds coverage,
/// and parallel worktree sessions run the same assemblies beside them. A pane that
/// loads its menu and chapter on the thread pool can then take well over a second
/// to land its first render. On bUnit's one second, the Desktop UI assembly failed
/// at least one wait in 14 of 15 runs under that load. Each such wait had made one
/// to four checks inside the second, and its condition held a moment later. Ten
/// seconds is the patience <see cref="Polling"/> already gives an off-render
/// condition, so the two kinds of wait agree.</para>
///
/// <para>A positive wait returns as soon as its condition holds, so a passing
/// test is no slower; only a test that is going to fail waits the longer time.
/// A negative wait (a <c>WaitForFailedException</c> the test expects) names its
/// own window and is not affected, and must keep doing so.</para>
///
/// <para>bUnit's default is a static, so it is set once per test process, before
/// any test runs. Each project that references bUnit links this file:
/// <c>Backlog.Desktop.UI.UnitTests</c>, <c>Backlog.UI.Components.UnitTests</c> and
/// <c>Backlog.Mobile.UI.UnitTests</c>.</para>
/// </summary>
internal static class BunitWaitTimeout
{
    public static TimeSpan Default { get; } = TimeSpan.FromSeconds(10);

    [ModuleInitializer]
    internal static void Configure() => BunitContext.DefaultWaitTimeout = Default;
}
