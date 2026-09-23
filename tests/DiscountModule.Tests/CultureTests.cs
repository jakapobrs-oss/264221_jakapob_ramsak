using System.Globalization;

using DiscountModule.Campaigns;
using DiscountModule.Models;
using DiscountModule.Validation;

namespace DiscountModule.Tests;

/// <summary>
/// The library may be used by an application running under any culture, so messages and descriptions
/// must not change with it (e.g. "0,005" instead of "0.005").
/// </summary>
public class CultureTests
{
    [Fact]
    public void Validation_messages_do_not_depend_on_the_current_culture()
    {
        Cart cart = new([new CartItem("Item", (ItemCategory)(-1), -0.005m, -1)]);
        Campaign[] campaigns = [new PercentageCoupon(100.5m), new PointsOnTop(-5), new SeasonalCampaign(300m, 400.5m)];

        DiscountValidationException exception = UnderCommaDecimalCulture(
            () => Assert.Throws<DiscountValidationException>(() => DiscountCalculator.Calculate(cart, campaigns)));

        string[] expected =
        [
            "Item #1 'Item': unit price must not be negative (got -0.005)",
            "Item #1 'Item': unit price must have at most 2 decimal places (got -0.005)",
            "Item #1 'Item': quantity must be at least 1 (got -1)",
            "Item #1 'Item': category must be one of Clothing, Accessories, Electronics (got -1)",
            "Campaign #1 'Percentage discount': percentage must be greater than 0 and at most 100 (got 100.5)",
            "Campaign #2 'Discount by points': points must be at least 1 (got -5)",
            "Campaign #3 'Special campaign': discount amount must not exceed every amount (got 400.5 > 300)",
        ];
        Assert.Equal(expected, exception.Errors);
    }

    [Fact]
    public void Descriptions_do_not_depend_on_the_current_culture()
    {
        // 830 - 12.5% (103.75) = 726.25; points cap floor(145.25) = 145 -> 581.25; one seasonal step -> 541.25.
        DiscountResult result = UnderCommaDecimalCulture(() => DiscountCalculator.Calculate(
            SampleCarts.TShirtHatAndBelt(),
            [new PercentageCoupon(12.5m), new PointsOnTop(200), new SeasonalCampaign(300m, 40m)]));

        string[] expected =
        [
            "12.5% off 830.00 THB",
            "200 points (cap 145 = 20% of 726.25 THB)",
            "40.00 THB off every 300.00 THB of 581.25 THB (1 x 40.00)",
        ];
        Assert.Equal(expected, result.AppliedDiscounts.Select(step => step.Description));
        Assert.Equal(541.25m, result.FinalPrice);
    }

    /// <summary>Runs <paramref name="action"/> under a culture that writes 1.5 as "1,5" and -1 as "~1".</summary>
    private static T UnderCommaDecimalCulture<T>(Func<T> action)
    {
        CultureInfo commaDecimal = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaDecimal.NumberFormat.NumberDecimalSeparator = ",";
        commaDecimal.NumberFormat.NegativeSign = "~";

        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = commaDecimal;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
