using System.Globalization;

using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Connectors page's value in the settings menu: how many task sources are
/// connected, zero included — the count the page's headline says in words.
/// </summary>
/// <remarks>
/// Follows the connected targets wherever they were changed, by passing their
/// <c>Changed</c> through. A host that keeps no targets draws no badge.
/// </remarks>
public sealed class ConnectedTaskSourcesBadgeSource(LinkedTaskSources? sources = null) : ISettingsBadgeSource
{
    public string? Badge => sources?.Targets?.List().Count.ToString(CultureInfo.InvariantCulture);

    public event Action? Changed
    {
        add { if (sources?.Targets is { } targets) targets.Changed += value; }
        remove { if (sources?.Targets is { } targets) targets.Changed -= value; }
    }
}
