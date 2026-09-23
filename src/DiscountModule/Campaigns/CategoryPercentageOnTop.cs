using DiscountModule.Models;

namespace DiscountModule.Campaigns;

/// <summary>On Top: subtracts a percentage (0-100) of the price of every item in one category.</summary>
public sealed record CategoryPercentageOnTop(ItemCategory ItemCategory, decimal Percentage) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.OnTop;
    public override string Name => "Percentage discount by item category";

    public override IEnumerable<string> Validate()
    {
        if (!Enum.IsDefined(ItemCategory))
        {
            string supported = string.Join(", ", Enum.GetNames<ItemCategory>());
            yield return $"item category must be one of {supported} (got {ItemCategory})";
        }

        if (Percentage <= 0 || Percentage > 100)
        {
            yield return $"percentage must be greater than 0 and at most 100 (got {Percentage})";
        }
    }

    // A3: the percentage is taken from the listed prices of the matching items, so a coupon applied
    // earlier does not shrink it. The calculator still caps the result at the running total.
    public override decimal ComputeDiscount(DiscountContext context)
        => CategoryTotal(context.Cart) * (Percentage / 100m);

    public override string Describe(DiscountContext context)
        => $"{Percentage:0.##}% off {ItemCategory} ({Money.Format(CategoryTotal(context.Cart))} THB)";

    private decimal CategoryTotal(Cart cart)
        => cart.Items.Where(item => item.Category == ItemCategory).Sum(item => item.LineTotal);
}
