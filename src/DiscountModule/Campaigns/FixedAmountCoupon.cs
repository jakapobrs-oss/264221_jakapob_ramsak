using DiscountModule.Validation;

namespace DiscountModule.Campaigns;

/// <summary>Coupon: subtracts a fixed amount (THB) from the order total.</summary>
public sealed record FixedAmountCoupon(decimal Amount) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.Coupon;
    public override string Name => "Fixed amount";

    public override IEnumerable<string> Validate() => AmountRules.Positive("amount", Amount);

    // A coupon larger than the order is fine: the calculator caps it, so the price stops at 0 (A6).
    public override decimal ComputeDiscount(DiscountContext context) => Amount;

    public override string Describe(DiscountContext context) => $"{Money.Format(Amount)} THB off";
}
