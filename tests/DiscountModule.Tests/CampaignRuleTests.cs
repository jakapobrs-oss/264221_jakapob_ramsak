using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Tests;

/// <summary>Caps, edge cases and rounding of the individual campaign rules.</summary>
public class CampaignRuleTests
{
    [Fact]
    public void A_fixed_amount_larger_than_the_cart_brings_the_price_to_zero_not_below()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [new FixedAmountCoupon(1000m)]);

        AppliedDiscount step = Assert.Single(result.AppliedDiscounts);
        Assert.Equal(600m, step.DiscountAmount);
        Assert.Equal(0m, result.FinalPrice);
    }

    [Fact]
    public void Points_above_the_cap_are_limited_to_20_percent_of_the_total()
    {
        // 20% of 830 = 166, so only 166 of the 200 points are used.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtHatAndBelt(), [new PointsOnTop(200)]);

        Assert.Equal(166, result.PointsUsed);
        Assert.Equal(664m, result.FinalPrice);
    }

    [Fact]
    public void The_points_cap_is_rounded_down_to_whole_baht()
    {
        // 20% of 762.50 = 152.50, and points are whole units, so the cap is 152.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(762.50m), [new PointsOnTop(200)]);

        Assert.Equal(152, result.PointsUsed);
        Assert.Equal(610.50m, result.FinalPrice);
    }

    [Fact]
    public void The_points_cap_is_based_on_the_total_after_the_coupon()
    {
        // 830 - 330 = 500, and 20% of 500 = 100.
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(), [new FixedAmountCoupon(330m), new PointsOnTop(200)]);

        Assert.Equal(100, result.PointsUsed);
        Assert.Equal(400m, result.FinalPrice);
    }

    [Fact]
    public void No_points_are_used_when_20_percent_of_the_total_is_under_one_baht()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(4.99m), [new PointsOnTop(10)]);

        Assert.Equal(0, result.PointsUsed);
        Assert.Equal(4.99m, result.FinalPrice);
    }

    [Fact]
    public void Seasonal_discount_counts_only_complete_steps()
    {
        // 899.99 contains 300 twice, so 2 x 40 = 80.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(899.99m), [new SeasonalCampaign(300m, 40m)]);

        Assert.Equal(819.99m, result.FinalPrice);
    }

    [Fact]
    public void Seasonal_discount_below_the_first_step_is_zero_but_still_listed()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(299.99m), [new SeasonalCampaign(300m, 40m)]);

        AppliedDiscount step = Assert.Single(result.AppliedDiscounts);
        Assert.Equal(0m, step.DiscountAmount);
        Assert.Equal(299.99m, result.FinalPrice);
    }

    [Fact]
    public void Seasonal_discount_never_exceeds_the_total_even_when_it_equals_the_step()
    {
        // 40 off every 40 on 100: 2 steps = 80, leaving the 20 that do not fill a step.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(100m), [new SeasonalCampaign(40m, 40m)]);

        Assert.Equal(20m, result.FinalPrice);
    }

    [Fact]
    public void Category_discount_with_no_matching_items_is_zero_but_still_listed()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            SampleCarts.TShirtAndHat(), [new CategoryPercentageOnTop(ItemCategory.Electronics, 15m)]);

        AppliedDiscount step = Assert.Single(result.AppliedDiscounts);
        Assert.Equal(0m, step.DiscountAmount);
        Assert.Equal(600m, result.FinalPrice);
    }

    [Fact]
    public void Category_discount_uses_the_listed_prices_even_after_a_coupon()
    {
        // 200 - 10% = 180, then 10% of the listed Clothing price (100, not 90) = 10.
        Cart cart = new([
            new CartItem("Shirt", ItemCategory.Clothing, 100m),
            new CartItem("Cap", ItemCategory.Accessories, 100m),
        ]);

        DiscountResult result = DiscountCalculator.Calculate(
            cart, [new PercentageCoupon(10m), new CategoryPercentageOnTop(ItemCategory.Clothing, 10m)]);

        Assert.Equal(10m, result.AppliedDiscounts[1].DiscountAmount);
        Assert.Equal(170m, result.FinalPrice);
    }

    [Fact]
    public void Category_discount_is_capped_at_what_is_left_after_the_coupon()
    {
        // 350 - 300 = 50 left, while 100% of the T-Shirt would be 350.
        DiscountResult result = DiscountCalculator.Calculate(
            new Cart([SampleCarts.TShirt]),
            [new FixedAmountCoupon(300m), new CategoryPercentageOnTop(ItemCategory.Clothing, 100m)]);

        Assert.Equal(50m, result.AppliedDiscounts[1].DiscountAmount);
        Assert.Equal(0m, result.FinalPrice);
    }

    [Fact]
    public void A_coupon_and_a_category_discount_never_lower_the_price_of_other_categories()
    {
        // 1,100 - 1,000 = 100 left. The jacket makes up 1,000 of the 1,100, so its share of what is left is
        // 90.91: 100% off Clothing stops there, and the cable still costs its own share, 9.09.
        Cart cart = new([
            new CartItem("Jacket", ItemCategory.Clothing, 1000m),
            new CartItem("Cable", ItemCategory.Electronics, 100m),
        ]);

        DiscountResult result = DiscountCalculator.Calculate(
            cart, [new FixedAmountCoupon(1000m), new CategoryPercentageOnTop(ItemCategory.Clothing, 100m)]);

        Assert.Equal(90.91m, result.AppliedDiscounts[1].DiscountAmount);
        Assert.Equal(9.09m, result.FinalPrice);
    }

    [Fact]
    public void A_category_discount_after_a_percentage_coupon_stops_at_the_category_share()
    {
        // 2,000 - 50% = 1,000 left, half of it the jacket's. 80% of the jacket's listed 1,000 would be 800,
        // more than the 500 left of its price, so the discount is 500 and the phone still costs its 500.
        Cart cart = new([
            new CartItem("Jacket", ItemCategory.Clothing, 1000m),
            new CartItem("Phone", ItemCategory.Electronics, 1000m),
        ]);

        DiscountResult result = DiscountCalculator.Calculate(
            cart, [new PercentageCoupon(50m), new CategoryPercentageOnTop(ItemCategory.Clothing, 80m)]);

        AppliedDiscount categoryStep = result.AppliedDiscounts[1];
        Assert.Equal(500m, categoryStep.DiscountAmount);
        Assert.Equal(
            "80% off Clothing (1,000.00 THB), capped at 500.00 THB (the Clothing share of the 1,000.00 THB left)",
            categoryStep.Description);
        Assert.Equal(500m, result.FinalPrice);
    }

    [Fact]
    public void A_category_discount_on_a_cart_of_free_items_is_zero()
    {
        DiscountResult result = DiscountCalculator.Calculate(
            new Cart([new CartItem("Sample", ItemCategory.Clothing, 0m)]),
            [new CategoryPercentageOnTop(ItemCategory.Clothing, 50m)]);

        Assert.Equal(0m, Assert.Single(result.AppliedDiscounts).DiscountAmount);
        Assert.Equal(0m, result.FinalPrice);
    }

    [Fact]
    public void The_category_share_is_exact_at_the_subtotal_limit()
    {
        // Subtotal 999,999,999,999,999.99, half of it plus 0.005 Clothing; the coupon leaves 0.01 less. The
        // Clothing share of what is left is 499,999,999,999,999.994999... and rounds down. Multiplying first would
        // overflow decimal, and dividing first would round the quotient to 28 digits, ending in ...995, which
        // would then round up a second time.
        Cart cart = new([
            new CartItem("Gold", ItemCategory.Clothing, InputLimits.MaxAmount, 500_000),
            new CartItem("Silver", ItemCategory.Accessories, InputLimits.MaxAmount, 499_999),
            new CartItem("Silver", ItemCategory.Accessories, 999_999_999.99m),
        ]);

        DiscountResult result = DiscountCalculator.Calculate(
            cart, [new FixedAmountCoupon(0.01m), new CategoryPercentageOnTop(ItemCategory.Clothing, 100m)]);

        Assert.Equal(999_999_999_999_999.99m, result.Subtotal);
        Assert.Equal(499_999_999_999_999.99m, result.AppliedDiscounts[1].DiscountAmount);
        Assert.Equal(499_999_999_999_999.99m, result.FinalPrice);
    }

    [Fact]
    public void A_category_share_of_exactly_half_a_satang_rounds_away_from_zero()
    {
        // 800 - 700 = 100 left, and the pin makes up 1 of the 800, so its share is 100 x 1 / 800 = 0.125:
        // exactly half a satang, which rounds to 0.13 (A8), not to 0.12.
        Cart cart = new([
            new CartItem("Pin", ItemCategory.Clothing, 1m),
            new CartItem("Bag", ItemCategory.Accessories, 799m),
        ]);

        DiscountResult result = DiscountCalculator.Calculate(
            cart, [new FixedAmountCoupon(700m), new CategoryPercentageOnTop(ItemCategory.Clothing, 100m)]);

        Assert.Equal(0.13m, result.AppliedDiscounts[1].DiscountAmount);
        Assert.Equal(99.87m, result.FinalPrice);
    }

    [Fact]
    public void Quantity_multiplies_the_unit_price_in_the_subtotal_and_in_the_category_total()
    {
        // 2 x 350 + 250 = 950; 15% of the two T-Shirts (700) = 105.
        Cart cart = new([SampleCarts.TShirt with { Quantity = 2 }, SampleCarts.Hat]);

        DiscountResult result = DiscountCalculator.Calculate(cart, [new CategoryPercentageOnTop(ItemCategory.Clothing, 15m)]);

        Assert.Equal(950m, result.Subtotal);
        Assert.Equal(845m, result.FinalPrice);
    }

    [Fact]
    public void A_free_item_is_accepted_and_adds_nothing_to_the_subtotal()
    {
        Cart cart = new([new CartItem("Sticker", ItemCategory.Accessories, 0m), SampleCarts.TShirt]);

        DiscountResult result = DiscountCalculator.Calculate(cart, [new PercentageCoupon(10m)]);

        Assert.Equal(350m, result.Subtotal);
        Assert.Equal(315m, result.FinalPrice);
    }

    [Fact]
    public void Discounts_are_rounded_to_satang()
    {
        // 10% of 333.33 = 33.333, rounded to 33.33.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(333.33m), [new PercentageCoupon(10m)]);

        Assert.Equal(33.33m, Assert.Single(result.AppliedDiscounts).DiscountAmount);
        Assert.Equal(300.00m, result.FinalPrice);
    }

    [Fact]
    public void Half_a_satang_is_rounded_away_from_zero()
    {
        // 10% of 0.05 = 0.005, rounded to 0.01 (banker's rounding would give 0.00).
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(0.05m), [new PercentageCoupon(10m)]);

        Assert.Equal(0.01m, Assert.Single(result.AppliedDiscounts).DiscountAmount);
        Assert.Equal(0.04m, result.FinalPrice);
    }

    [Fact]
    public void Just_under_half_a_satang_is_rounded_down()
    {
        // 0.49% of 1.00 = 0.0049, rounded to 0.00, so the price stays 1.00.
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.SingleItem(1.00m), [new PercentageCoupon(0.49m)]);

        Assert.Equal(0.00m, Assert.Single(result.AppliedDiscounts).DiscountAmount);
        Assert.Equal(1.00m, result.FinalPrice);
    }

    [Fact]
    public void Discounts_on_the_largest_accepted_order_are_still_exact()
    {
        // Subtotal 999,999,999,999,950.01 (just under InputLimits.MaxSubtotal). 99.99% of it is exactly
        // 999,899,999,999,950.014999, which must round down to .01. If decimal rounded the product first,
        // .014999 could become .015 and then round up to .02.
        Cart cart = new([
            new CartItem("Gold", ItemCategory.Accessories, 999_999_999.99m, InputLimits.MaxQuantity),
            new CartItem("Silver", ItemCategory.Accessories, 9_950.01m),
        ]);

        DiscountResult result = DiscountCalculator.Calculate(cart, [new PercentageCoupon(99.99m)]);

        Assert.Equal(999_999_999_999_950.01m, result.Subtotal);
        Assert.Equal(999_899_999_999_950.01m, Assert.Single(result.AppliedDiscounts).DiscountAmount);
        Assert.Equal(100_000_000_000.00m, result.FinalPrice);
    }

    [Fact]
    public void Without_campaigns_the_final_price_is_the_subtotal()
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), []);

        Assert.Empty(result.AppliedDiscounts);
        Assert.Equal(600m, result.FinalPrice);
        Assert.Equal(0m, result.TotalDiscount);
        Assert.Equal(0, result.PointsUsed);
    }
}
