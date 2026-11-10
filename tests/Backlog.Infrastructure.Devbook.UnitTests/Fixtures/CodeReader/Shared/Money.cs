namespace Shop.Shared;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero => new(0, "EUR");

    public static Money operator +(Money left, Money right) => left with { Amount = left.Amount + right.Amount };
}
