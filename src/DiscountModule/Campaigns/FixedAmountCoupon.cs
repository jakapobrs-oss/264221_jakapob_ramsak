namespace DiscountModule.Campaigns;

/// <summary>Coupon: subtracts a fixed amount (THB) from the order total.</summary>
public sealed record FixedAmountCoupon(decimal Amount) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.Coupon;
    public override string Name => "Fixed amount";

    public override IEnumerable<string> Validate() => throw new NotImplementedException();

    public override decimal ComputeDiscount(DiscountContext context) => throw new NotImplementedException();
}
