namespace DiscountModule.Campaigns;

/// <summary>Coupon: subtracts a percentage (0-100) of the order total.</summary>
public sealed record PercentageCoupon(decimal Percentage) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.Coupon;
    public override string Name => "Percentage discount";

    public override IEnumerable<string> Validate()
    {
        if (Percentage <= 0 || Percentage > 100)
        {
            yield return $"percentage must be greater than 0 and at most 100 (got {Percentage})";
        }
    }

    // Dividing first keeps the intermediate value below the running total, so it cannot overflow.
    public override decimal ComputeDiscount(DiscountContext context)
        => context.RunningTotal * (Percentage / 100m);

    public override string Describe(DiscountContext context)
        => $"{Percentage:0.##}% off {Money.Format(context.RunningTotal)} THB";
}
