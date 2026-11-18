using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Backlog.Desktop.Services;

/// <summary>
/// Backlog's icon in the Windows notification area (local ADR 0043 §5): the
/// executable's own icon, the main window's title as its tooltip, a right-click menu
/// with <b>Open Backlog</b> and <b>Quit</b>, and a double-click — or Enter on the
/// focused icon — that opens the main window. Hand-written <c>Shell_NotifyIcon</c> interop, because the Windows App SDK
/// has no notify-icon API and a package for one icon is more than it is worth.
/// <list type="bullet">
/// <item>The icon's messages go to a window of its own that is never shown. Not a
/// message-only window: those receive no broadcast, and both <c>TaskbarCreated</c> —
/// which says Explorer restarted and the icon must be added again — and the
/// sign-out and shutdown messages are broadcasts.</item>
/// <item>Created on the main thread, whose message loop the app already runs, so
/// every callback arrives there.</item>
/// <item>Removed on <see cref="Dispose"/>, and on process exit as a backstop, so no
/// dead icon lingers until the person hovers over it.</item>
/// </list>
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = WmApp + 1;
    private const uint IconId = 1;
    private const int OpenCommand = 1;
    private const int QuitCommand = 2;

    private readonly Action _open;
    private readonly Action _quit;
    private readonly Action<bool> _sessionEnding;
    private readonly string _tooltip;
    private readonly WndProc _wndProc;
    private readonly string _className;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private readonly bool _ownsIcon;
    private nint _window;
    private bool _disposed;

    /// <summary>Whether the icon is in the notification area now: false when Windows
    /// refused it, until a later <c>TaskbarCreated</c> adds it.</summary>
    public bool IsShown { get; private set; }

    private TrayIcon(string tooltip, Action open, Action quit, Action<bool> sessionEnding)
    {
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _open = open;
        _quit = quit;
        _sessionEnding = sessionEnding;

        // Kept in a field for the window's life: a delegate the garbage collector
        // reclaims while Windows still calls it is a crash with no stack.
        _wndProc = WindowProcedure;
        _className = "Backlog.TrayIcon." + Guid.NewGuid().ToString("N");

        var instance = GetModuleHandleW(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            Instance = instance,
            ClassName = _className,
        };

        if (RegisterClassExW(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        _window = CreateWindowExW(0, _className, string.Empty, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_window == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        (_icon, _ownsIcon) = ExecutableIcon();

        Add();
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>The tray icon, or null when Windows refused one. Closing the main
    /// window hides it only while <see cref="IsShown"/>: with no icon to bring it back,
    /// a close quits as it used to.</summary>
    /// <param name="tooltip">The main window's title, so a Debug build keeps the name of
    /// the worktree it came from.</param>
    /// <param name="open">Open Backlog, and a double-click.</param>
    /// <param name="quit">Quit.</param>
    /// <param name="sessionEnding">Windows asks whether it may sign out or shut down
    /// (false), then says it will (true).</param>
    public static TrayIcon? TryCreate(string tooltip, Action open, Action quit, Action<bool> sessionEnding)
    {
        try
        {
            return new TrayIcon(tooltip, open, quit, sessionEnding);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"The tray icon could not be created: {exception}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Remove();

        if (_window != 0)
        {
            _ = DestroyWindow(_window);
            _window = 0;
        }

        _ = UnregisterClassW(_className, GetModuleHandleW(null));
        if (_ownsIcon && _icon != 0) _ = DestroyIcon(_icon);
    }

    /// <summary>Only the icon: the window belongs to the main thread, which may be
    /// gone by now, and the process is ending anyway.</summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        if (!_disposed) Remove();
    }

    private void Add()
    {
        var data = Data();
        data.Flags = NifMessage | NifIcon | NifTip | NifShowTip;
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        data.Tip = _tooltip;

        // TaskbarCreated also arrives on a taskbar or DPI change that left the icon in
        // place, where an add fails on the icon already there: modifying it is the
        // answer to whether it is shown then.
        IsShown = ShellNotifyIconW(NimAdd, ref data) || ShellNotifyIconW(NimModify, ref data);
        if (!IsShown) return;

        // Version 4: the event comes in the low word of lParam and the pointer's
        // position in wParam, and a right-click arrives as WM_CONTEXTMENU.
        data.Version = NotifyIconVersion4;
        _ = ShellNotifyIconW(NimSetVersion, ref data);
    }

    private void Remove()
    {
        if (_window == 0) return;

        var data = Data();
        _ = ShellNotifyIconW(NimDelete, ref data);
        IsShown = false;
    }

    private NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = IconId,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (message == CallbackMessage)
            {
                switch ((int)(lParam & 0xFFFF))
                {
                    case WmLButtonDblClk:
                    case NinKeySelect:
                        _open();
                        break;
                    case WmContextMenu:
                        ShowMenu(window, (short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                        break;
                }

                return 0;
            }

            if (message == _taskbarCreated && _taskbarCreated != 0)
            {
                // Explorer restarted and forgot every icon.
                Add();
                return 0;
            }

            switch (message)
            {
                case WmQueryEndSession:
                    _sessionEnding(false);
                    return 1;
                case WmEndSession when wParam != 0:
                    _sessionEnding(true);
                    return 0;
            }
        }
        catch (Exception exception)
        {
            // Nothing a Windows callback throws may cross back into native code.
            System.Diagnostics.Debug.WriteLine($"The tray icon failed to handle message {message}: {exception}");
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void ShowMenu(nint window, int x, int y)
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;

        try
        {
            _ = AppendMenuW(menu, MfString, OpenCommand, "Open Backlog");
            _ = AppendMenuW(menu, MfString, QuitCommand, "Quit");
            _ = SetMenuDefaultItem(menu, OpenCommand, 0);

            // The documented dance: the menu closes on a click elsewhere only while its
            // owner is the foreground window, and the posted message lets it close at all.
            _ = SetForegroundWindow(window);
            var command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | TpmBottomAlign, x, y, window, 0);
            _ = PostMessageW(window, WmNull, 0, 0);

            switch (command)
            {
                case OpenCommand:
                    _open();
                    break;
                case QuitCommand:
                    _quit();
                    break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    /// <summary>The executable's own icon at the small size, or the system's
    /// application icon when it carries none.</summary>
    private static (nint Icon, bool Owned) ExecutableIcon()
    {
        if (Environment.ProcessPath is { } path)
        {
            var small = new nint[1];
            if (ExtractIconExW(path, 0, null, small, 1) > 0 && small[0] != 0) return (small[0], true);
        }

        return (LoadIconW(0, IdiApplication), false);
    }

    private const int WmApp = 0x8000;
    private const int WmNull = 0x0000;
    private const int WmContextMenu = 0x007B;
    private const int WmLButtonDblClk = 0x0203;

    /// <summary>Enter or Space on the icon when it has the keyboard focus.</summary>
    private const int NinKeySelect = 0x0401;
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifShowTip = 0x80;
    private const uint MfString = 0x0;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const nint IdiApplication = 32512;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint Version;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, nint[]? large, nint[]? small, uint icons);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nint id, string text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIconW(nint instance, nint iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
