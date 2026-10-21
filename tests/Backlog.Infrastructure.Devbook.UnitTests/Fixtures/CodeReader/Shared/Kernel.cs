using Shop.Orders;

namespace Shop.Shared
{
    public abstract class EntityBase
    {
        protected EntityBase()
        {
        }

        public event EventHandler? Changed;
    }

    public interface IAuditable
    {
        DateTimeOffset Modified { get; }
    }

    public class Repository<T> where T : EntityBase
    {
        public T? Find(Guid id) => default;
    }

    internal enum Status
    {
        Open,
        Closed
    }

    public delegate void OrderHandler(Order order);
}
