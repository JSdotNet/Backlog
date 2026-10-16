using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Tests;

/// <summary>
/// A model price store held in memory: it answers the table it was given and
/// keeps whatever is saved. Shared because the Sessions pane and its settings
/// page are tested in <c>Backlog.Modules.Sessions.UnitTests</c> while the
/// Settings menu badges are tested in <c>Backlog.Desktop.UI.UnitTests</c>.
/// </summary>
/// <param name="table">The table the store answers until something is saved.</param>
internal sealed class StubPriceStore(ModelPriceTable table) : IModelPriceStore
{
    public ModelPriceTable Table { get; private set; } = table;

    public Task<ModelPriceTable> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Table);

    public Task SaveAsync(ModelPriceTable saved, CancellationToken cancellationToken = default)
    {
        Table = saved;
        return Task.CompletedTask;
    }
}
