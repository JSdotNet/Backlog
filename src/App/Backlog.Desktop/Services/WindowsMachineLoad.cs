using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.Services;

/// <summary>
/// This desktop's CPU and free memory, for a loop's <see cref="CapacityReading"/> (local
/// ADR 0051 §4). CPU is the busy share of <c>GetSystemTimes</c> over the last 30 seconds
/// (<see cref="CpuWindow"/>), sampled every <see cref="SampleEvery"/> from the first read on
/// for the app's life, so no read waits 30 seconds and none averages over the hours since
/// the one before. The first read has nothing sampled before it, so it samples over
/// <see cref="FirstWindow"/>. Free memory is <c>GlobalMemoryStatusEx</c>'s available
/// physical memory. A call that fails leaves its figure null.
/// </summary>
/// <param name="clock">What drives the sampler and stamps the samples; the system clock when null.</param>
[SupportedOSPlatform("windows")]
public sealed class WindowsMachineLoad(TimeProvider? clock = null) : IMachineLoad, IDisposable
{
    /// <summary>The first read's window: long enough to smooth a scheduler tick, short
    /// enough not to stall a check-in.</summary>
    internal static readonly TimeSpan FirstWindow = TimeSpan.FromSeconds(1);

    /// <summary>How often the sampler reads the CPU times: one syscall, six to a window.</summary>
    internal static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(5);

    private const double BytesPerGb = 1024d * 1024 * 1024;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly CpuWindow _window = new(clock);
    private readonly Lock _gate = new();
    private ITimer? _sampler;

    public async Task<MachineLoad> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (StartSampler())
        {
            await Task.Delay(FirstWindow, _clock, cancellationToken).ConfigureAwait(false);
        }

        // A fresh sample, so the window ends now rather than at the sampler's last tick.
        Sample();

        return new(_window.BusyPercent(), FreeMemoryGb());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _sampler?.Dispose();
            _sampler = null;
        }
    }

    /// <summary>Takes the first sample and starts the sampler, on the first read only;
    /// answers whether this was it.</summary>
    private bool StartSampler()
    {
        lock (_gate)
        {
            if (_sampler is not null) return false;

            Sample();
            _sampler = _clock.CreateTimer(_ => Sample(), null, SampleEvery, SampleEvery);
            return true;
        }
    }

    private void Sample()
    {
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) _window.Add(new CpuTimes(idle, kernel, user));
    }

    private static double? FreeMemoryGb()
    {
        var status = new NativeMethods.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };

        return NativeMethods.GlobalMemoryStatusEx(ref status) ? status.AvailablePhysical / BytesPerGb : null;
    }

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
