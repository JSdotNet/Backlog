using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The rates the person enters in Settings, in <c>backlog.db</c>: empty until somebody
/// fills them in, every rate back exactly as typed, an empty rate kept empty, and a
/// save replacing the table whole.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteModelPriceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-model-prices", Guid.NewGuid().ToString("n"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_table_is_empty_until_somebody_fills_it_in()
    {
        Assert.Same(ModelPriceTable.Empty, await new SqliteModelPriceStore(_root).GetAsync(Ct));
    }

    [Fact]
    public async Task A_saved_table_comes_back_in_its_order_with_every_rate_as_typed()
    {
        var store = new SqliteModelPriceStore(_root);
        var table = new ModelPriceTable(
        [
            new ModelPrice("claude-opus-5-5", 5m, 25m, 0.50m, 6.25m),
            new ModelPrice("haiku", 1m, 5m, 0.10m, null)
        ]);

        await store.SaveAsync(table, Ct);

        var read = await store.GetAsync(Ct);
        Assert.Equal(table.Prices, read.Prices);
        Assert.Equal("0.50", read.Prices[0].CacheReadPerMTok!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(read.Prices[1].CacheWritePerMTok);
    }

    [Fact]
    public async Task A_save_replaces_the_table_and_drops_a_row_with_no_model()
    {
        var store = new SqliteModelPriceStore(_root);

        await store.SaveAsync(new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m)]), Ct);
        await store.SaveAsync(new ModelPriceTable([new ModelPrice("sonnet", 3m, 15m, 0.3m, 3.75m), new ModelPrice(" ", 1m, 1m, 1m, 1m)]), Ct);

        Assert.Equal(["sonnet"], (await store.GetAsync(Ct)).Prices.Select(price => price.Model));
    }

    [Fact]
    public void AddSqlite_registers_a_rooted_price_store_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddSqlite(_ => _root);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IModelPriceStore));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<RootedSqliteModelPriceStore>(provider.GetRequiredService<IModelPriceStore>());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
