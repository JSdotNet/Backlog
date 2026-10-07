using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The monthly spend budgets on disk, on <see cref="UsageResetSettingsStoreTests"/>'
/// terms: nothing until somebody says, one amount per provider that survives a
/// restart, and no file once every budget is cleared.
/// </summary>
public class SpendBudgetSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "spend-budget-settings-store-tests-" + Guid.NewGuid().ToString("N"));

    public SpendBudgetSettingsStoreTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "spend-budgets.json");

    private SpendBudgetSettingsStore Store() => new(SettingsFile);

    [Fact]
    public void An_untouched_store_has_no_budget_and_no_file()
    {
        var store = Store();

        Assert.Null(store.BudgetFor(SpendProvider.Claude));
        Assert.Null(store.BudgetFor(SpendProvider.Copilot));
        Assert.Null(store.BudgetFor(SpendProvider.AzureFoundry));
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void Each_provider_keeps_its_own_budget_across_a_restart()
    {
        var store = Store();
        Assert.Null(store.Set(SpendProvider.Claude, 100m));
        Assert.Null(store.Set(SpendProvider.AzureFoundry, 49.5m));

        var reopened = Store();

        Assert.Equal(100m, reopened.BudgetFor(SpendProvider.Claude));
        Assert.Null(reopened.BudgetFor(SpendProvider.Copilot));
        Assert.Equal(49.5m, reopened.BudgetFor(SpendProvider.AzureFoundry));
    }

    [Fact]
    public void Clearing_the_last_budget_removes_the_file()
    {
        var store = Store();
        _ = store.Set(SpendProvider.Copilot, 20m);

        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.Set(SpendProvider.Copilot, null));

        Assert.Null(store.BudgetFor(SpendProvider.Copilot));
        Assert.False(File.Exists(SettingsFile));
        Assert.Equal(1, raised);

        // Clearing what is already clear is not a change.
        Assert.Null(store.Set(SpendProvider.Copilot, null));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_negative_budget_is_refused_and_the_one_in_force_stays()
    {
        var store = Store();
        _ = store.Set(SpendProvider.Claude, 10m);

        Assert.NotNull(store.Set(SpendProvider.Claude, -1m));

        Assert.Equal(10m, store.BudgetFor(SpendProvider.Claude));
    }

    /// <summary>One entry the store cannot read costs that entry, not the others.</summary>
    [Fact]
    public void An_unreadable_entry_is_dropped_and_the_rest_are_kept()
    {
        File.WriteAllText(SettingsFile, """{ "Claude": 80, "Gemini": 10, "Copilot": -3, "7": 1 }""");

        var store = Store();

        Assert.Equal(80m, store.BudgetFor(SpendProvider.Claude));
        Assert.Null(store.BudgetFor(SpendProvider.Copilot));
        Assert.Null(store.BudgetFor(SpendProvider.AzureFoundry));
    }

    [Fact]
    public void A_file_that_is_not_json_reads_as_no_budgets()
    {
        File.WriteAllText(SettingsFile, "not json");

        Assert.Null(Store().BudgetFor(SpendProvider.Claude));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
