namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// Which person's data this is. Every stored row and every query in this module
/// is scoped to one of these.
/// <para>
/// A type of its own rather than a bare <see cref="Guid"/>, which is the
/// exception to how the rest of the solution names ids and is worth the
/// exception here: an owner id and a device id are both Guids, both travel
/// together through every call, and swapping them would not fail to compile —
/// it would hand one device the run of somebody else's data. That is the one
/// mistake in this module the compiler should be made to catch.
/// </para>
/// </summary>
public readonly record struct OwnerId(Guid Value)
{
    public static OwnerId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

/// <summary>One machine belonging to an owner. Identifies the caller; it grants
/// nothing on its own.</summary>
public readonly record struct DeviceId(Guid Value)
{
    public static DeviceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}
