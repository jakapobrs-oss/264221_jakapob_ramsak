using DiscountModule.Campaigns;
using DiscountModule.Validation;

using static System.FormattableString;

namespace DiscountModule.Tests;

/// <summary>
/// Shows that a new kind of campaign needs no change to <see cref="DiscountCalculator"/>. The campaign
/// below exists only in this test project to demonstrate the design; it is not a feature of the module.
/// </summary>
public class ExtensibilityTests
{
    /// <summary>EXAMPLE ONLY: an On Top campaign that takes a fixed amount off for members.</summary>
    private sealed record MemberFlatOnTop(decimal Amount) : Campaign
    {
        public override CampaignCategory Category => CampaignCategory.OnTop;
        public override string Name => "Member discount";

        public override IEnumerable<string> Validate()
        {
            if (Amount <= 0)
            {
                yield return Invariant($"amount must be greater than 0 (got {Amount})");
            }
        }

        public override decimal ComputeDiscount(DiscountContext context) => Amount;

        public override string Describe(DiscountContext context) => Invariant($"{Amount:0.00} THB off for members");
    }

    /// <summary>EXAMPLE ONLY: a faulty campaign whose rule comes out negative.</summary>
    private sealed record SurchargeOnTop : Campaign
    {
        public override CampaignCategory Category => CampaignCategory.OnTop;
        public override string Name => "Faulty discount";

        public override IEnumerable<string> Validate() => [];

        public override decimal ComputeDiscount(DiscountContext context) => -50m;

        public override string Describe(DiscountContext context) => "50.00 THB added by mistake";
    }

    [Fact]
    public void A_new_campaign_type_takes_its_categorys_place_in_the_order()
    {
        // Passed last, applied second: Coupon -> On Top (the new type) -> Seasonal.
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(),
            [new SeasonalCampaign(300m, 40m), new PercentageCoupon(10m), new MemberFlatOnTop(100m)]);

        (string Name, decimal PriceBefore, decimal DiscountAmount, decimal PriceAfter, string Description)[] expected =
        [
            ("Percentage discount", 830m, 83m, 747m, "10% off 830.00 THB"),
            ("Member discount", 747m, 100m, 647m, "100.00 THB off for members"),
            ("Special campaign", 647m, 80m, 567m, "40.00 THB off every 300.00 THB of 647.00 THB (2 x 40.00)"),
        ];
        Assert.Equal(expected, result.AppliedDiscounts.Select(step =>
            (step.Campaign.Name, step.PriceBefore, step.DiscountAmount, step.PriceAfter, step.Description)));
    }

    [Fact]
    public void A_new_campaign_type_is_capped_at_what_is_left()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new MemberFlatOnTop(1000m)]);

        Assert.Equal(600m, Assert.Single(result.AppliedDiscounts).DiscountAmount);
        Assert.Equal(0m, result.FinalPrice);
    }

    [Fact]
    public void A_campaign_that_computes_a_negative_discount_takes_nothing_off_and_adds_nothing()
    {
        // A step never adds to the price (A6), whatever a campaign computes.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new SurchargeOnTop()]);

        AppliedDiscount step = Assert.Single(result.AppliedDiscounts);
        Assert.Equal(0m, step.DiscountAmount);
        Assert.Equal(600m, step.PriceAfter);
        Assert.Equal(600m, result.FinalPrice);
    }

    [Fact]
    public void A_new_campaign_type_is_validated_like_the_built_in_ones()
    {
        DiscountValidationException exception = Assert.Throws<DiscountValidationException>(
            () => DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new MemberFlatOnTop(0m)]));

        Assert.Equal("Campaign #1 'Member discount': amount must be greater than 0 (got 0)", Assert.Single(exception.Errors));
    }

    [Fact]
    public void A_new_campaign_type_counts_for_the_one_per_category_rule()
    {
        DiscountValidationException exception = Assert.Throws<DiscountValidationException>(
            () => DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new MemberFlatOnTop(50m), new PointsOnTop(10)]));

        Assert.Equal(
            "Only one On Top campaign can be applied per order (got: Member discount, Discount by points)",
            Assert.Single(exception.Errors));
    }
}
