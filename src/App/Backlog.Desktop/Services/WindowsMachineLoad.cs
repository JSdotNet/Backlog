using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.Services;

/// <summary>
/// This desktop's CPU and free memory, for a loop's <see cref="CapacityReading"/> (local
/// ADR 0051 §4). CPU is the busy share of <c>GetSystemTimes</c> since the previous read;
/// the first read has no previous one, so it samples over <see cref="FirstWindow"/>.
/// Free memory is <c>GlobalMemoryStatusEx</c>'s available physical memory. A call that
/// fails leaves its figure null.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMachineLoad : IMachineLoad
{
    /// <summary>The first read's window: long enough to smooth a scheduler tick, short
    /// enough not to stall a check-in.</summary>
    internal static readonly TimeSpan FirstWindow = TimeSpan.FromSeconds(1);

    private const double BytesPerGb = 1024d * 1024 * 1024;

    private readonly Lock _gate = new();
    private Sample? _previous;
    private double? _lastCpu;

    public async Task<MachineLoad> ReadAsync(CancellationToken cancellationToken = default)
    {
        var sample = TakeSample();

        if (sample is { } first && IsFirst(first))
        {
            await Task.Delay(FirstWindow, cancellationToken).ConfigureAwait(false);
            sample = TakeSample();
        }

        return new(Cpu(sample), FreeMemoryGb());
    }

    /// <summary>Records the sample as the first one when there was none before.</summary>
    private bool IsFirst(Sample sample)
    {
        lock (_gate)
        {
            if (_previous is not null) return false;

            _previous = sample;
            return true;
        }
    }

    /// <summary>The busy share since the previous sample, which this one replaces. With no
    /// time passed between them, the last share read.</summary>
    private double? Cpu(Sample? current)
    {
        if (current is not { } now) return null;

        lock (_gate)
        {
            var before = _previous;
            _previous = now;

            if (before is not { } then) return null;

            // Kernel time includes idle time, so the total is kernel plus user.
            var total = (now.Kernel - then.Kernel) + (now.User - then.User);
            var idle = now.Idle - then.Idle;

            if (total == 0) return _lastCpu;

            _lastCpu = Math.Clamp((total - idle) * 100d / total, 0, 100);
            return _lastCpu;
        }
    }

    private static Sample? TakeSample() =>
        NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)
            ? new Sample(idle, kernel, user)
            : null;

    private static double? FreeMemoryGb()
    {
        var status = new NativeMethods.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };

        return NativeMethods.GlobalMemoryStatusEx(ref status) ? status.AvailablePhysical / BytesPerGb : null;
    }

    /// <summary>One <c>GetSystemTimes</c> reading, in 100-nanosecond units.</summary>
    private readonly record struct Sample(ulong Idle, ulong Kernel, ulong User);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        // Each FILETIME is two 32-bit halves, low first, so it reads as one ulong.
        [DllImport("kernel32.dll", EntryPoint = "GetSystemTimes", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

        [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
