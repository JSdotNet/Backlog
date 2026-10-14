using System.Globalization;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// The Model prices page's value in the settings menu: how many models have a
/// price, zero included — the count the page's headline says in words.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IModelPriceStore"/> is read asynchronously and raises no event of its
/// own, so this reads it once, the first time the menu asks, and draws nothing
/// until the read lands; then <see cref="Changed"/> is raised from the thread it
/// landed on. The Model prices page is the one place the table is written, and it
/// hands each saved count to <see cref="Report"/>, which moves the badge at once.
/// </para>
/// <para>
/// A host that keeps no prices draws no badge, and neither does a store that could
/// not be read: the page says why.
/// </para>
/// </remarks>
public sealed class ModelPriceBadgeSource(IModelPriceStore? store = null) : ISettingsBadgeSource
{
    private readonly Lock _gate = new();
    private int? _count;
    private bool _reading;

    public string? Badge
    {
        get
        {
            if (store is null) return null;

            lock (_gate)
            {
                if (_count is { } count) return count.ToString(CultureInfo.InvariantCulture);
                if (_reading) return null;

                _reading = true;
            }

            // Off the render thread: the store's read is file I/O.
            _ = Task.Run(() => ReadAsync(store));
            return null;
        }
    }

    public event Action? Changed;

    /// <summary>Records <paramref name="count"/> prices as the table's, after the
    /// page read or saved it, and raises <see cref="Changed"/>.</summary>
    public void Report(int count)
    {
        lock (_gate) _count = count;

        Changed?.Invoke();
    }

    private async Task ReadAsync(IModelPriceStore prices)
    {
        ModelPriceTable table;

        try
        {
            table = await prices.GetAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The page reads the table again and says what went wrong; the menu
            // keeps quiet rather than guess, and asks again the next time it draws.
            lock (_gate) _reading = false;
            return;
        }

        lock (_gate)
        {
            // A save reported while the read was out is newer than what it read.
            if (_count is not null) return;
            _count = table.Prices.Count;
        }

        Changed?.Invoke();
    }
}
