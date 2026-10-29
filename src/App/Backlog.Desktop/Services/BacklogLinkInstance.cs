using Backlog.Desktop.UI.Shell;
using Backlog.SharedKernel;

using Microsoft.Windows.AppLifecycle;

using Windows.ApplicationModel.Activation;

namespace Backlog.Desktop.Services;

/// <summary>
/// Where a <c>backlog://</c> activation of the desktop head goes (local ADR 0043 §7).
/// The packaged build registers the protocol in <c>Package.appxmanifest</c>; Windows
/// then starts a new process for every link opened. That process asks, before the app
/// is built, whether an instance is already running: when one is, the activation is
/// redirected to it and this process ends; when none is, this instance is the one later
/// links are redirected to, and its own link waits for the main window.
/// <list type="bullet">
/// <item>Only a launch that carries a link is redirected. A plain launch starts as it
/// always did, so nothing about starting the app changes for anyone who never opens a
/// link.</item>
/// <item>An unpackaged (Debug) build registers no protocol, but reads a
/// <c>backlog://window/…</c> argument on its command line the same way, so a worktree's
/// build can be tried without installing a package. Its instance key carries the
/// folder it runs from, so a link never lands in another worktree's app.</item>
/// </list>
/// Static, because it runs in the head's <c>Program.Main</c>, ahead of the container:
/// it holds what arrives until <see cref="Attach"/> hands it to
/// <see cref="BacklogLinkActivation"/>.
/// </summary>
internal static class BacklogLinkInstance
{
    private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static readonly List<string> Waiting = [];
    private static BacklogLinkActivation? _activation;

    /// <summary>
    /// Claims this launch: true when its link was handed to the instance already
    /// running, and this process is to end without starting the app; false when this
    /// process is to start, its own link — if it has one — waiting for
    /// <see cref="Attach"/>.
    /// </summary>
    public static bool RedirectToRunningInstance()
    {
        try
        {
            var current = AppInstance.GetCurrent();
            var arguments = current.GetActivatedEventArgs();
            var link = LinkOf(arguments, ownLaunch: true);

            // Listening before the key is claimed: a link redirected here the moment it
            // is would otherwise find no one to hear it.
            current.Activated += (_, redirected) =>
            {
                if (LinkOf(redirected, ownLaunch: false) is { } later) Hand(later);
            };

            var main = AppInstance.FindOrRegisterForKey(InstanceKey());
            if (!main.IsCurrent)
            {
                if (link is null) return false;

                // The running instance may bring its new window to the front: Windows
                // grants the foreground only to a process the foreground one names.
                _ = AllowSetForegroundWindow(main.ProcessId);

                // Off this thread and waited for, briefly: a running instance that does
                // not answer leaves this process to end rather than hang without a window.
                _ = Task.Run(() => main.RedirectActivationToAsync(arguments).AsTask()).Wait(RedirectTimeout);
                return true;
            }

            if (link is not null) Hand(link);
        }
        catch (Exception exception)
        {
            // No log exists yet. A launch the Windows App SDK cannot place starts as a
            // plain one: a link is lost, never the app.
            System.Diagnostics.Debug.WriteLine($"Claiming the Backlog instance failed: {exception}");
        }

        return false;
    }

    /// <summary>Hands every link that arrived so far, and every later one, to
    /// <paramref name="activation"/>.</summary>
    public static void Attach(BacklogLinkActivation activation)
    {
        string[] waiting;
        lock (Gate)
        {
            _activation = activation;
            waiting = [.. Waiting];
            Waiting.Clear();
        }

        foreach (var link in waiting) activation.Activate(link);
    }

    private static void Hand(string link)
    {
        BacklogLinkActivation? activation;
        lock (Gate)
        {
            activation = _activation;
            if (activation is null)
            {
                Waiting.Add(link);
                return;
            }
        }

        activation.Activate(link);
    }

    /// <summary>The <c>backlog://</c> link an activation carries: the URI of a protocol
    /// activation, or a window link on the command line of a launch.</summary>
    /// <param name="ownLaunch">True for this process's own launch, whose command line
    /// is read when the activation carries no arguments; false for one redirected from
    /// another process, whose command line this process cannot read.</param>
    private static string? LinkOf(AppActivationArguments arguments, bool ownLaunch)
    {
        switch (arguments.Kind)
        {
            case ExtendedActivationKind.Protocol when arguments.Data is IProtocolActivatedEventArgs protocol
                && string.Equals(protocol.Uri?.Scheme, SurfaceLink.Scheme, StringComparison.OrdinalIgnoreCase):
                return protocol.Uri!.OriginalString;

            case ExtendedActivationKind.Launch when arguments.Data is ILaunchActivatedEventArgs launch:
                return LinkOnCommandLine(launch.Arguments, ownLaunch);

            default:
                return null;
        }
    }

    /// <summary>The first window link among the launch's arguments, or, for this
    /// process's own launch, on its command line, which an unpackaged launch may leave
    /// out of the activation's arguments.</summary>
    private static string? LinkOnCommandLine(string? arguments, bool ownLaunch)
    {
        string[] candidates = !string.IsNullOrWhiteSpace(arguments)
            ? arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ownLaunch ? Environment.GetCommandLineArgs() : [];

        return candidates.FirstOrDefault(candidate => SurfaceLink.Parse(candidate) is not null)?.Trim().Trim('"');
    }

    /// <summary>The key the running instance registers under. A packaged install is one
    /// app; a Debug build runs out of a worktree, often beside others, so its key names
    /// the folder it runs from.</summary>
    private static string InstanceKey()
    {
#if DEBUG
        var folder = AppContext.BaseDirectory.TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(folder)))[..16];
        return "backlog-main-" + hash;
#else
        return "backlog-main";
#endif
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
