using Shop.Shared;

namespace Shop.Orders;

internal class OrderService
{
    private readonly Repository<Order> _orders;

    public OrderService(Repository<Order> orders) => _orders = orders;

    public Order? Find(Guid id) => _orders.Find(id);

    private sealed class Cache
    {
        public Order? Last;
    }
}
