using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Win32;

namespace Backlog.Desktop.Services;

/// <summary>
/// This desktop's reading of the person's input, for <see cref="Presence"/> (local ADR 0041):
/// the last keyboard or mouse input from <c>GetLastInputInfo</c>, sampled each time it is
/// read, and whether the screen is locked, from the session-switch events Windows raises on
/// lock and unlock.
/// <para>
/// Nothing is polled and nothing leaves the process. The lock state starts unlocked, because
/// the application is started by somebody at the keyboard; a lock that happened before it
/// started is caught by the input's age instead.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInputActivity : IInputActivity, IDisposable
{
    private readonly TimeProvider _clock;
    private volatile bool _locked;

    public WindowsInputActivity(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public DateTimeOffset? LastInputAt
    {
        get
        {
            var info = new NativeMethods.LastInputInfo { Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>() };

            if (!NativeMethods.GetLastInputInfo(ref info)) return null;

            // Both are GetTickCount values, milliseconds since boot in 32 bits; the
            // unsigned difference stays right across the 49.7-day wrap.
            var idle = unchecked((uint)Environment.TickCount - info.Time);

            return _clock.GetUtcNow() - TimeSpan.FromMilliseconds(idle);
        }
    }

    public bool IsScreenLocked => _locked;

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                _locked = true;
                break;
            case SessionSwitchReason.SessionUnlock:
                _locked = false;
                break;
        }
    }

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [DllImport("user32.dll", EntryPoint = "GetLastInputInfo")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetLastInputInfo(ref LastInputInfo info);
    }
}
