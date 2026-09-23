using System.Globalization;

using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Tests;

/// <summary>The shape of the result: a consistent step-by-step breakdown, and inputs left untouched.</summary>
public class BreakdownTests
{
    [Fact]
    public void Every_step_starts_where_the_previous_one_ended()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(),
            [new SeasonalCampaign(300m, 40m), new PointsOnTop(100), new PercentageCoupon(10m)]);

        decimal expectedPriceBefore = result.Subtotal;
        foreach (AppliedDiscount step in result.AppliedDiscounts)
        {
            Assert.Equal(expectedPriceBefore, step.PriceBefore);
            Assert.Equal(step.PriceBefore - step.DiscountAmount, step.PriceAfter);
            expectedPriceBefore = step.PriceAfter;
        }

        Assert.Equal(expectedPriceBefore, result.FinalPrice);
        Assert.Equal(263m, result.TotalDiscount); // 830 - 567
    }

    [Fact]
    public void Each_step_explains_how_its_discount_was_computed()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(),
            [new PercentageCoupon(10m), new PointsOnTop(200), new SeasonalCampaign(300m, 40m)]);

        string[] expected =
        [
            "10% off 830.00 THB",
            "200 points (cap 149 = 20% of 747.00 THB)",
            "40.00 THB off every 300.00 THB of 598.00 THB (1 x 40.00)",
        ];
        Assert.Equal(expected, result.AppliedDiscounts.Select(step => step.Description));
    }

    [Fact]
    public void Fixed_amount_and_category_steps_explain_their_base_amount()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.MixedCategories(),
            [new FixedAmountCoupon(50m), new CategoryPercentageOnTop(ItemCategory.Clothing, 15m)]);

        string[] expected = ["50.00 THB off", "15% off Clothing (1,050.00 THB)"];
        Assert.Equal(expected, result.AppliedDiscounts.Select(step => step.Description));
    }

    [Fact]
    public void Every_amount_in_the_result_carries_exactly_two_decimal_places()
    {
        // Prices given as whole numbers (350, 250) and a 10% step (60.0) must still come out as satang.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new PercentageCoupon(10m)]);

        AppliedDiscount step = Assert.Single(result.AppliedDiscounts);
        string[] expected = ["600.00", "600.00", "60.00", "540.00", "540.00", "60.00"];
        decimal[] amounts = [result.Subtotal, step.PriceBefore, step.DiscountAmount, step.PriceAfter, result.FinalPrice, result.TotalDiscount];
        Assert.Equal(expected, amounts.Select(amount => amount.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void The_input_cart_and_campaign_list_are_not_changed()
    {
        List<CartItem> items = [SampleCarts.TShirt, SampleCarts.Hat, SampleCarts.Belt];
        CartItem[] itemsBefore = [.. items];
        List<Campaign> campaigns = [new SeasonalCampaign(300m, 40m), new PercentageCoupon(10m)];
        Campaign[] campaignsBefore = [.. campaigns];

        _ = DiscountCalculator.Calculate(new Cart(items), campaigns);

        Assert.Equal(itemsBefore, items);
        // The coupon is applied first, but the caller's list keeps its own order.
        Assert.Equal(campaignsBefore, campaigns);
    }
}
