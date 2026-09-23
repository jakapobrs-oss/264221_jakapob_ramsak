using DiscountModule.Models;
using DiscountModule.Validation;

using static System.FormattableString;

namespace DiscountModule.Campaigns;

/// <summary>
/// On Top: subtracts a percentage (0-100) of the listed price of every item in one category, but never more
/// than what is left of that category's price after the campaigns before it.
/// </summary>
public sealed record CategoryPercentageOnTop(ItemCategory ItemCategory, decimal Percentage) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.OnTop;
    public override string Name => "Percentage discount by item category";

    public override IEnumerable<string> Validate()
    {
        if (!Enum.IsDefined(ItemCategory))
        {
            string supported = string.Join(", ", Enum.GetNames<ItemCategory>());
            yield return Invariant($"item category must be one of {supported} (got {(int)ItemCategory})");
        }

        foreach (string problem in PercentageRules.Validate(Percentage))
        {
            yield return problem;
        }
    }

    // A3: the percentage is taken from the listed prices of the matching items, capped at the category's share
    // of the running total. A coupon applied earlier has already lowered every item's price; without the cap,
    // the coupon and this campaign together could take more than the category's items are worth, and the rest
    // would come off the items of other categories.
    public override decimal ComputeDiscount(DiscountContext context)
        => Math.Min(ListedPriceDiscount(context.Cart), ShareLeft(context));

    public override string Describe(DiscountContext context)
    {
        string description = Invariant($"{Percentage:0.##}% off {ItemCategory} ({Money.Format(CategoryTotal(context.Cart))} THB)");
        decimal shareLeft = ShareLeft(context);
        return shareLeft < ListedPriceDiscount(context.Cart)
            ? description + Invariant($", capped at {Money.Format(shareLeft)} THB (the {ItemCategory} share of the {Money.Format(context.RunningTotal)} THB left)")
            : description;
    }

    // Exact for the same reason as PercentageCoupon: at most 2 decimal places in the percentage.
    private decimal ListedPriceDiscount(Cart cart) => CategoryTotal(cart) * (Percentage / 100m);

    // The running total spread over the items in proportion to their listed prices: this category's part of it.
    // A cart of free items only has nothing left to share.
    private decimal ShareLeft(DiscountContext context)
    {
        decimal subtotal = context.Cart.Subtotal;
        return subtotal == 0 ? 0m : Money.Share(context.RunningTotal, CategoryTotal(context.Cart), subtotal);
    }

    private decimal CategoryTotal(Cart cart)
        => cart.Items.Where(item => item.Category == ItemCategory).Sum(item => item.LineTotal);
}
