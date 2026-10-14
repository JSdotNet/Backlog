using System.Globalization;

using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// The Inbox page's value in the settings menu: how many routing rules there are,
/// zero included — the count the page's headline says in words.
/// </summary>
/// <remarks>
/// Follows the rules wherever they were written, by passing
/// <see cref="IInboxRoutingRules.Changed"/> through. A host that keeps no rules
/// registers no store, and then the page draws no badge, as it draws no field.
/// </remarks>
public sealed class InboxRoutingRulesBadgeSource(IInboxRoutingRules? rules = null) : ISettingsBadgeSource
{
    public string? Badge => rules?.Current.Count.ToString(CultureInfo.InvariantCulture);

    public event Action? Changed
    {
        add { if (rules is not null) rules.Changed += value; }
        remove { if (rules is not null) rules.Changed -= value; }
    }
}
