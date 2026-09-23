using DiscountModule.Campaigns;
using DiscountModule.Models;
using DiscountModule.Validation;

namespace DiscountModule.Tests;

/// <summary>Application order (Coupon, On Top, Seasonal) and the one-campaign-per-category rule.</summary>
public class CampaignOrderTests
{
    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(0, 2, 1)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(2, 1, 0)]
    public void The_order_in_which_campaigns_are_passed_does_not_change_the_result(int first, int second, int third)
    {
        Campaign[] campaigns = [new PercentageCoupon(10m), new PointsOnTop(100), new SeasonalCampaign(300m, 40m)];

        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(), [campaigns[first], campaigns[second], campaigns[third]]);

        CampaignCategory[] expectedOrder = [CampaignCategory.Coupon, CampaignCategory.OnTop, CampaignCategory.Seasonal];
        Assert.Equal(expectedOrder, result.AppliedDiscounts.Select(step => step.Campaign.Category));
        Assert.Equal(567m, result.FinalPrice);
    }

    [Fact]
    public void Fixed_amount_and_percentage_coupons_cannot_be_used_together()
    {
        DiscountValidationException exception = Assert.Throws<DiscountValidationException>(() => DiscountCalculator.Calculate(
            SampleCarts.TShirtAndHat(), [new FixedAmountCoupon(50m), new PercentageCoupon(10m)]));

        Assert.Equal(
            "Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)",
            Assert.Single(exception.Errors));
    }

    [Fact]
    public void Category_percentage_and_points_cannot_be_used_together_because_both_are_on_top()
    {
        DiscountValidationException exception = Assert.Throws<DiscountValidationException>(() => DiscountCalculator.Calculate(
            SampleCarts.TShirtAndHat(), [new CategoryPercentageOnTop(ItemCategory.Clothing, 15m), new PointsOnTop(50)]));

        Assert.Equal(
            "Only one On Top campaign can be applied per order (got: Percentage discount by item category, Discount by points)",
            Assert.Single(exception.Errors));
    }

    [Fact]
    public void Two_seasonal_campaigns_cannot_be_used_together()
    {
        DiscountValidationException exception = Assert.Throws<DiscountValidationException>(() => DiscountCalculator.Calculate(
            SampleCarts.TShirtAndHat(), [new SeasonalCampaign(300m, 40m), new SeasonalCampaign(500m, 100m)]));

        Assert.Equal(
            "Only one Seasonal campaign can be applied per order (got: Special campaign, Special campaign)",
            Assert.Single(exception.Errors));
    }
}
