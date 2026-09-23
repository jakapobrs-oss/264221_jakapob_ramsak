using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Tests;

/// <summary>The five examples from the assignment, plus combined orders that use all three categories.</summary>
public class WorkedExampleTests
{
    [Fact]
    public void Fixed_amount_coupon_of_50_on_600_gives_550()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new FixedAmountCoupon(50m)]);

        Assert.Equal(550m, result.FinalPrice);
    }

    [Fact]
    public void Percentage_coupon_of_10_percent_on_600_gives_540()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new PercentageCoupon(10m)]);

        Assert.Equal(540m, result.FinalPrice);
    }

    [Fact]
    public void Fifteen_percent_off_clothing_on_2540_gives_2382_50()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.MixedCategories(), [new CategoryPercentageOnTop(ItemCategory.Clothing, 15m)]);

        Assert.Equal(2540m, result.Subtotal);
        Assert.Equal(2382.50m, result.FinalPrice);
    }

    [Fact]
    public void Spending_68_points_on_830_gives_762()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtHatAndBelt(), [new PointsOnTop(68)]);

        Assert.Equal(762m, result.FinalPrice);
        Assert.Equal(68, result.PointsUsed);
    }

    [Fact]
    public void Forty_off_every_300_on_830_gives_750()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtHatAndBelt(), [new SeasonalCampaign(300m, 40m)]);

        Assert.Equal(750m, result.FinalPrice);
    }

    [Fact]
    public void Ten_percent_then_100_points_then_seasonal_on_830_gives_567()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(),
            [new PercentageCoupon(10m), new PointsOnTop(100), new SeasonalCampaign(300m, 40m)]);

        (string Name, decimal PriceBefore, decimal DiscountAmount, decimal PriceAfter)[] expectedSteps =
        [
            ("Percentage discount", 830m, 83m, 747m),
            ("Discount by points", 747m, 100m, 647m), // cap floor(149.40) = 149, so all 100 points are used
            ("Special campaign", 647m, 80m, 567m), // floor(647 / 300) = 2 steps
        ];
        Assert.Equal(expectedSteps, Steps(result));
        Assert.Equal(567m, result.FinalPrice);
        Assert.Equal(100, result.PointsUsed);
    }

    [Fact]
    public void Ten_percent_then_200_points_then_seasonal_on_830_gives_558()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(),
            [new PercentageCoupon(10m), new PointsOnTop(200), new SeasonalCampaign(300m, 40m)]);

        (string Name, decimal PriceBefore, decimal DiscountAmount, decimal PriceAfter)[] expectedSteps =
        [
            ("Percentage discount", 830m, 83m, 747m),
            ("Discount by points", 747m, 149m, 598m), // capped at floor(149.40) = 149 of the 200 points
            ("Special campaign", 598m, 40m, 558m), // floor(598 / 300) = 1 step
        ];
        Assert.Equal(expectedSteps, Steps(result));
        Assert.Equal(558m, result.FinalPrice);
        Assert.Equal(149, result.PointsUsed);
    }

    private static IEnumerable<(string Name, decimal PriceBefore, decimal DiscountAmount, decimal PriceAfter)> Steps(DiscountResult result)
        => result.AppliedDiscounts.Select(step => (step.Campaign.Name, step.PriceBefore, step.DiscountAmount, step.PriceAfter));
}
