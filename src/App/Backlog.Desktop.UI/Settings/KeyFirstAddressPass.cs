using System.Runtime.CompilerServices;

using Backlog.Infrastructure.AzureMaps;
using Backlog.Modules.Events.Abstractions;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The pass the Azure Maps key's first save starts over the addresses saved before it
/// (<c>.devbook/domain/events/domain.md#address-lookup</c>), held once per app so at most one
/// runs at a time: a first save made while it runs - the key forgotten and saved again, or
/// saved from Settings in another window - joins it rather than sending the same waiting
/// addresses to Azure Maps a second time at once (local ADR 0061 §5).
/// <para>
/// One per key store rather than a registered service: the store is the app's one singleton
/// behind the key, so every window's Settings page reaches the same pass through it, and a
/// host or test with a store of its own gets a pass of its own. The table holds the store
/// weakly, so the pass goes when the store does.
/// </para>
/// </summary>
public sealed class KeyFirstAddressPass
{
    private static readonly ConditionalWeakTable<AzureMapsSettingsStore, KeyFirstAddressPass> PerStore = new();

    private readonly Lock _gate = new();

    private Task<AddressLookupPassDto>? _pass;

    private KeyFirstAddressPass()
    {
    }

    /// <summary>The pass of the app that keeps its key in <paramref name="store"/>.</summary>
    public static KeyFirstAddressPass For(AzureMapsSettingsStore store) => PerStore.GetValue(store, _ => new KeyFirstAddressPass());

    /// <summary>The pass still running, or null when none is.</summary>
    public Task<AddressLookupPassDto>? Running
    {
        get
        {
            lock (_gate)
            {
                return _pass is { IsCompleted: false } running ? running : null;
            }
        }
    }

    /// <summary>
    /// The running pass when there is one; otherwise <paramref name="pass"/>, started off the
    /// caller's thread so the save that asked does not wait on the geocoder.
    /// </summary>
    public Task<AddressLookupPassDto> Start(Func<Task<AddressLookupPassDto>> pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        lock (_gate)
        {
            if (_pass is { IsCompleted: false } running) return running;

            _pass = Task.Run(pass);
            return _pass;
        }
    }
}
