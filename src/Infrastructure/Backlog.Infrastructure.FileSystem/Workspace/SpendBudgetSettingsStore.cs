using System.Text.Json;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

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

    private readonly string _path;

    private readonly Dictionary<SpendProvider, decimal> _budgets;

    public SpendBudgetSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "spend-budgets.json"))
    {
    }

    public SpendBudgetSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _budgets = Read();
    }

    public event Action? Changed;

    public string SettingsPath => _path;

    public decimal? BudgetFor(SpendProvider provider) =>
        _budgets.TryGetValue(provider, out var amount) ? amount : null;

    public string? Set(SpendProvider provider, decimal? amount)
    {
        if (amount is < 0m) return "A budget can't be negative.";

        if (BudgetFor(provider) == amount) return null;

        if (amount is { } value) _budgets[provider] = value;
        else _budgets.Remove(provider);

        string? error = null;

        try
        {
            if (_budgets.Count == 0)
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            else
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(
                    _budgets.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
                    JsonOptions));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the budget couldn't be saved for next time.";
        }

        Changed?.Invoke();

        return error;
    }

    /// <summary>A provider name the enum no longer has, or a negative amount, is
    /// dropped rather than failing the whole file: one bad entry should not cost the
    /// person the budgets that are still good.</summary>
    private Dictionary<SpendProvider, decimal> Read()
    {
        var budgets = new Dictionary<SpendProvider, decimal>();

        try
        {
            if (!File.Exists(_path)) return budgets;

            var stored = JsonSerializer.Deserialize<Dictionary<string, decimal>>(File.ReadAllText(_path), JsonOptions);

            foreach (var (name, amount) in stored ?? [])
            {
                if (Enum.TryParse<SpendProvider>(name, ignoreCase: true, out var provider)
                    && Enum.IsDefined(provider)
                    && amount >= 0m)
                {
                    budgets[provider] = amount;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            budgets.Clear();
        }

        return budgets;
    }
}
