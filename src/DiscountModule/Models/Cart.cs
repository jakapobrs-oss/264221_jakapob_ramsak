namespace DiscountModule.Models;

/// <summary>The shopping cart handed to the discount module. The module never mutates it.</summary>
public sealed record Cart(IReadOnlyList<CartItem> Items)
{
    /// <summary>Sum of all line totals before any discount.</summary>
    public decimal Subtotal => Items.Sum(item => item.LineTotal);
}
