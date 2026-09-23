namespace DiscountModule.Campaigns;

/// <summary>Coupon: subtracts a percentage (0-100) of the order total.</summary>
public sealed record PercentageCoupon(decimal Percentage) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.Coupon;
    public override string Name => "Percentage discount";

    public override IEnumerable<string> Validate() => throw new NotImplementedException();

    public override decimal ComputeDiscount(DiscountContext context) => throw new NotImplementedException();
}
