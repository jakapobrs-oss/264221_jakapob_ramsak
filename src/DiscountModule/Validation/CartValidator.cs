using System.Globalization;

using DiscountModule.Models;

namespace DiscountModule.Validation;

/// <summary>
/// Checks the cart rules (design notes, section 5) and returns every problem found, not just the first.
/// It never computes line totals, so even absurd values are reported instead of overflowing.
/// </summary>
internal static class CartValidator
{
    private static readonly string SupportedCategories = string.Join(", ", Enum.GetNames<ItemCategory>());
    private static readonly string MaxQuantityText = InputLimits.MaxQuantity.ToString("N0", CultureInfo.InvariantCulture);

    public static IEnumerable<string> Validate(Cart? cart)
    {
        if (cart is null)
        {
            yield return "Cart is required";
            yield break;
        }

        // A10: applying discounts to an empty order is treated as a mistake.
        if (cart.Items is null || cart.Items.Count == 0)
        {
            yield return "Cart must contain at least one item";
            yield break;
        }

        for (int index = 0; index < cart.Items.Count; index++)
        {
            foreach (string error in ValidateItem(cart.Items[index], position: index + 1))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<string> ValidateItem(CartItem? item, int position)
    {
        if (item is null)
        {
            yield return $"Item #{position} is missing";
            yield break;
        }

        bool hasName = !string.IsNullOrWhiteSpace(item.Name);
        string label = hasName ? $"Item #{position} '{item.Name}'" : $"Item #{position}";

        if (!hasName)
        {
            yield return $"{label}: name must not be blank";
        }

        // A10: a free item (unit price 0) is allowed.
        foreach (string problem in AmountRules.NonNegative("unit price", item.UnitPrice))
        {
            yield return $"{label}: {problem}";
        }

        if (item.Quantity < 1)
        {
            yield return $"{label}: quantity must be at least 1 (got {item.Quantity})";
        }
        else if (item.Quantity > InputLimits.MaxQuantity)
        {
            yield return $"{label}: quantity is too large (max {MaxQuantityText}, got {item.Quantity})";
        }

        if (!Enum.IsDefined(item.Category))
        {
            yield return $"{label}: category must be one of {SupportedCategories} (got {item.Category})";
        }
    }
}
