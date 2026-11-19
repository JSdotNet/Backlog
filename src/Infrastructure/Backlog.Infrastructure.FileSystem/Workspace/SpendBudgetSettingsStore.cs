using System.Text.Json;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.SharedKernel.Storage;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The monthly spend budgets as a small JSON file beside the weekly usage reset, read
/// once and written on every change — <see cref="UsageResetSettingsStore"/>'s shape, for
/// the same reason: it is the person's choice on this device, not the workspace's.
/// </summary>
/// <remarks>
/// The file maps a provider's name to its amount — <c>{ "Claude": 100 }</c> — and holds
/// only the providers that have a budget. With none left it is deleted, so an unset
/// state looks the same on disk whether it was never set or cleared.
/// </remarks>
public sealed class SpendBudgetSettingsStore : ISpendBudgetSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly JsonFileStore<Dictionary<string, decimal>> _file;

    private readonly Dictionary<SpendProvider, decimal> _budgets;

    public SpendBudgetSettingsStore()
        : this(AppDataPaths.Shared("spend-budgets.json"))
    {
    }

    public SpendBudgetSettingsStore(string path)
    {
        _file = new JsonFileStore<Dictionary<string, decimal>>(path, JsonOptions);

        _budgets = Read();
    }

    public event Action? Changed;

    public string SettingsPath => _file.Path;

    public decimal? BudgetFor(SpendProvider provider) =>
        _budgets.TryGetValue(provider, out var amount) ? amount : null;

    public string? Set(SpendProvider provider, decimal? amount)
    {
        if (amount is < 0m) return "A budget can't be negative.";

        if (BudgetFor(provider) == amount) return null;

        if (amount is { } value) _budgets[provider] = value;
        else _budgets.Remove(provider);

        var saved = _file.TrySave(_budgets.Count == 0
            ? null
            : _budgets.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));

        var error = saved ? null : "Changed, but the budget couldn't be saved for next time.";

        Changed?.Invoke();

        return error;
    }

    /// <summary>A provider name the enum no longer has, or a negative amount, is
    /// dropped rather than failing the whole file: one bad entry should not cost the
    /// person the budgets that are still good.</summary>
    private Dictionary<SpendProvider, decimal> Read()
    {
        var budgets = new Dictionary<SpendProvider, decimal>();

        foreach (var (name, amount) in _file.Load() ?? [])
        {
            if (Enum.TryParse<SpendProvider>(name, ignoreCase: true, out var provider)
                && Enum.IsDefined(provider)
                && amount >= 0m)
            {
                budgets[provider] = amount;
            }
        }

        return budgets;
    }
}
