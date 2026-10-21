using Shop.Shared;

namespace Shop.Orders;

sealed partial class Order
{
    public Money Total() => Sum(Money.Zero);

    private static Money Sum(Money seed) => seed;
}
