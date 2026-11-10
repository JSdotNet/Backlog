using Shop.Shared;

namespace Shop.Orders;

public sealed record OrderLine(string Sku, int Quantity, Money Price)
{
    public Money Subtotal => Price;
}
