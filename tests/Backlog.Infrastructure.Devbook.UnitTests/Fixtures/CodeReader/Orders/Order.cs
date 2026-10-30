using Shop.Shared;

namespace Shop.Orders;

public partial class Order : EntityBase, IAuditable
{
    private readonly List<OrderLine> _lines = new();

    public Guid Id { get; } = Guid.NewGuid();

    public DateTimeOffset Modified { get; private set; }

    public void Add(OrderLine line)
    {
        _lines.Add(line);
        OnChanged();
    }

    internal int Count => _lines.Count;

    protected virtual void OnChanged() => Modified = DateTimeOffset.UtcNow;
}
