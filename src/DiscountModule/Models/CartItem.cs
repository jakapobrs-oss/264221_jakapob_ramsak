namespace DiscountModule.Models;

/// <summary>A line in the shopping cart. Prices are in THB.</summary>
public sealed record CartItem(string Name, ItemCategory Category, decimal UnitPrice, int Quantity = 1)
{
    /// <summary>Price of this line before any discount (unit price x quantity).</summary>
    public decimal LineTotal => UnitPrice * Quantity;
}
