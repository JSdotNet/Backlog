using System.Diagnostics.CodeAnalysis;
using Backlog.SharedKernel;

namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// What a <see cref="Machine"/> is known by: the device identity Sync pairs on,
/// minted once per installation and never reused (ADR 0005).
/// </summary>
/// <remarks>
/// A value of its own rather than a bare <see cref="Guid"/> or string, so a machine
/// name can no longer be passed where the identity is meant. The text form is the
/// <c>D</c> format the device identity store writes and every session record carries
/// as its environment id, so a parsed record and this device compare equal.
/// </remarks>
public readonly record struct MachineId(Guid Value)
{
    /// <summary>The identity as every record and per-machine folder spells it.</summary>
    public override string ToString() => Value.ToString("D");

    /// <summary>Reads an id a record or a folder carries. Anything that is not a
    /// device identity is refused rather than turned into one.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out MachineId id)
    {
        if (Guid.TryParse(text, out var value) && value != Guid.Empty)
        {
            id = new MachineId(value);
            return true;
        }

        id = default;
        return false;
    }
}

/// <summary>
/// One development PC, as Dev PC Management owns it (local ADR 0073 §7): built over
/// Sync's device identity, so the box is the same machine to the tools pane, to
/// Insights' filter and to a paired device.
/// </summary>
/// <param name="Id">The identity that survives a rename. What anything kept per
/// machine is keyed by.</param>
/// <param name="Name">What the operating system calls the box now. For a person to
/// recognise in a list, never a key: two machines can share a name and one machine
/// can be renamed.</param>
public sealed record Machine(MachineId Id, string Name)
{
    /// <summary>The machine a device identity names.</summary>
    public static Machine FromDevice(DeviceIdentity device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return new Machine(new MachineId(device.Id), device.Name);
    }
}

/// <summary>
/// PORT — the machines this installation knows.
/// <para>
/// This device, and every machine that has sent this one a session record: Sync
/// replicates those records, and nothing else here can name another box. An adapter
/// outside every module answers it, because the answer joins the kernel's device
/// identity to Sessions' records.
/// </para>
/// <para>
/// Insights reads it for the machine filter and the tools pane reads
/// <see cref="Current"/> for which per-PC folder is this machine's.
/// </para>
/// </summary>
public interface IMachineRegistry
{
    /// <summary>The machine the app is running on.</summary>
    Machine Current { get; }

    /// <summary>
    /// Every machine known since <paramref name="since"/>: <see cref="Current"/>
    /// first, then the machines whose records arrived, by name, each once by id.
    /// A record source that cannot answer leaves <see cref="Current"/> alone on
    /// the list; cancellation travels.
    /// </summary>
    Task<IReadOnlyList<Machine>> GetMachinesAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
