namespace Backlog.SharedKernel;

/// <summary>
/// Which machine this installation is, as a fact that survives being renamed.
/// </summary>
/// <param name="Id">
/// Minted once, on the first start that finds no identity, and never again. It is
/// what a session record, a pairing credential and — later — a sync tiebreak are
/// attached to, so it has to outlive every name the box is given.
/// </param>
/// <param name="Name">
/// What the operating system calls the machine right now. A display name, refreshed
/// from the OS on every start: it is the word a person recognises in a list, and it
/// is emphatically not the identity. Two machines can share a name and one machine
/// can be renamed, which is exactly why <see cref="Id"/> exists beside it.
/// </param>
public sealed record DeviceIdentity(Guid Id, string Name);

/// <summary>
/// PORT — the identity of the machine the app is running on.
/// <para>
/// In the kernel rather than in a context's abstractions because it is the
/// <c>IAppFeatureSettings</c> situation again: several contexts ask the question and
/// none of them owns the answer. Sessions stamps every record it reads with it, the
/// Dashboard offers it as a filter, and the sync slice will pair on it — and none of
/// those three may see either of the others. A port here is what lets all of them ask
/// without any of them reaching sideways.
/// </para>
/// <para>
/// A property rather than a method, and no refresh. The identity is read once when
/// the host composes the adapter and does not move while the app is open: a machine
/// is not renamed mid-session, and a watcher would buy a case nobody has against a
/// contract every consumer would then have to subscribe to. "Device" is the
/// product-wide word — see <c>.domain/tasks/naming.md#device</c>; Sessions keeps
/// calling it an Environment and the Dashboard calls it a Machine, because each
/// context keeps its own vocabulary and the kernel carries the product's.
/// </para>
/// </summary>
public interface IDeviceIdentitySource
{
    /// <summary>This machine, as of when the host composed the adapter.</summary>
    DeviceIdentity Current { get; }
}
