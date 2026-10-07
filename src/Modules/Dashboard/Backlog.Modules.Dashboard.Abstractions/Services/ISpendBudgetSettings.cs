using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// The person's monthly spend budget per provider, kept per device beside the weekly
/// usage reset (the <c>spend-budgets.json</c> setting).
/// <para>
/// One bare amount per provider, read in that provider's own currency — the currency
/// its spend report is in. No currency is stored, because Backlog holds no exchange
/// rate and a budget in a currency the provider does not bill in could never be set
/// against its spend. A provider with no amount has no budget.
/// </para>
/// </summary>
public interface ISpendBudgetSettings
{
    /// <summary>Raised after a change has taken effect, whether or not it could be
    /// saved for next time.</summary>
    event Action? Changed;

    /// <summary>Where the budgets are kept, so the settings screen can say.</summary>
    string SettingsPath { get; }

    /// <summary>The provider's monthly budget, or null when it has none.</summary>
    decimal? BudgetFor(SpendProvider provider);

    /// <summary>Sets the provider's monthly budget, or clears it when
    /// <paramref name="amount"/> is null. Returns a message when the amount was refused
    /// or the change took effect but could not be saved, and null otherwise.</summary>
    string? Set(SpendProvider provider, decimal? amount);
}
