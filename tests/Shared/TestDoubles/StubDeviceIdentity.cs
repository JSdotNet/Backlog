using Backlog.SharedKernel;

namespace Backlog.Tests;

/// <summary>
/// This machine's identity, fixed for the test: the empty id named <c>Workshop PC</c>,
/// unless the test hands it the identity to answer.
/// </summary>
/// <param name="current">What every reading answers; the empty id named
/// <c>Workshop PC</c> when omitted.</param>
internal sealed class StubDeviceIdentity(DeviceIdentity? current = null) : IDeviceIdentitySource
{
    public DeviceIdentity Current { get; } = current ?? new(Guid.Empty, "Workshop PC");
}
