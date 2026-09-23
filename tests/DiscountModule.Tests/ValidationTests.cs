using DiscountModule.Campaigns;
using DiscountModule.Models;
using DiscountModule.Validation;

namespace DiscountModule.Tests;

/// <summary>Every validation rule of the design notes (section 5), and how the errors are reported.</summary>
public class ValidationTests
{
    // ---- Cart ----

    [Fact]
    public void A_missing_cart_is_rejected()
    {
        Assert.Equal("Cart is required", SingleError(null!, []));
    }

    [Fact]
    public void A_cart_without_items_is_rejected()
    {
        Assert.Equal("Cart must contain at least one item", SingleError(new Cart([]), []));
    }

    [Fact]
    public void A_cart_whose_item_list_is_missing_is_rejected()
    {
        Assert.Equal("Cart must contain at least one item", SingleError(new Cart(null!), []));
    }

    [Fact]
    public void A_missing_item_is_reported_by_its_position()
    {
        Assert.Equal("Item #2 is missing", SingleError(new Cart([SampleCarts.TShirt, null!]), []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_item_name_must_not_be_blank(string? name)
    {
        Cart cart = new([new CartItem(name!, ItemCategory.Clothing, 350m)]);

        Assert.Equal("Item #1: name must not be blank", SingleError(cart, []));
    }

    [Fact]
    public void An_item_price_must_not_be_negative()
    {
        Cart cart = new([SampleCarts.TShirt with { UnitPrice = -1m }]);

        Assert.Equal("Item #1 'T-Shirt': unit price must not be negative (got -1)", SingleError(cart, []));
    }

    [Fact]
    public void An_item_price_must_not_exceed_the_limit()
    {
        Cart cart = new([SampleCarts.TShirt with { UnitPrice = 1_000_000_000.01m }]);

        Assert.Equal(
            "Item #1 'T-Shirt': unit price is too large (max 1,000,000,000, got 1000000000.01)",
            SingleError(cart, []));
    }

    [Fact]
    public void An_item_price_must_be_a_whole_number_of_satang()
    {
        Cart cart = new([SampleCarts.TShirt with { UnitPrice = 0.005m }]);

        Assert.Equal("Item #1 'T-Shirt': unit price must have at most 2 decimal places (got 0.005)", SingleError(cart, []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_item_quantity_must_be_at_least_one(int quantity)
    {
        Cart cart = new([SampleCarts.TShirt, SampleCarts.Hat with { Quantity = quantity }]);

        Assert.Equal($"Item #2 'Hat': quantity must be at least 1 (got {quantity})", SingleError(cart, []));
    }

    [Fact]
    public void An_item_quantity_must_not_exceed_the_limit()
    {
        Cart cart = new([SampleCarts.TShirt with { Quantity = 1_000_001 }]);

        Assert.Equal("Item #1 'T-Shirt': quantity is too large (max 1,000,000, got 1000001)", SingleError(cart, []));
    }

    [Fact]
    public void An_item_category_must_be_a_known_category()
    {
        Cart cart = new([SampleCarts.TShirt with { Category = (ItemCategory)99 }]);

        Assert.Equal(
            "Item #1 'T-Shirt': category must be one of Clothing, Accessories, Electronics (got 99)",
            SingleError(cart, []));
    }

    [Fact]
    public void Absurdly_large_values_are_reported_instead_of_overflowing()
    {
        Cart cart = new([new CartItem("Gold", ItemCategory.Accessories, decimal.MaxValue, int.MaxValue)]);

        string[] expected =
        [
            "Item #1 'Gold': unit price is too large (max 1,000,000,000, got 79228162514264337593543950335)",
            "Item #1 'Gold': quantity is too large (max 1,000,000, got 2147483647)",
        ];
        Assert.Equal(expected, Reject(cart, []).Errors);
    }

    // ---- Campaign list ----

    [Fact]
    public void A_missing_campaign_list_is_rejected()
    {
        Assert.Equal("Campaign list is required", SingleError(SampleCarts.TShirtAndHat(), null!));
    }

    [Fact]
    public void A_missing_campaign_is_reported_by_its_position()
    {
        Assert.Equal("Campaign #2 is missing", SingleError(SampleCarts.TShirtAndHat(), [new FixedAmountCoupon(50m), null!]));
    }

    public static TheoryData<Campaign, string> InvalidCampaigns => new()
    {
        { new FixedAmountCoupon(0m), "Campaign #1 'Fixed amount': amount must be greater than 0 (got 0)" },
        { new FixedAmountCoupon(-50m), "Campaign #1 'Fixed amount': amount must be greater than 0 (got -50)" },
        { new FixedAmountCoupon(1_000_000_000.01m), "Campaign #1 'Fixed amount': amount is too large (max 1,000,000,000, got 1000000000.01)" },
        { new FixedAmountCoupon(0.005m), "Campaign #1 'Fixed amount': amount must have at most 2 decimal places (got 0.005)" },
        { new PercentageCoupon(0m), "Campaign #1 'Percentage discount': percentage must be greater than 0 and at most 100 (got 0)" },
        { new PercentageCoupon(-10m), "Campaign #1 'Percentage discount': percentage must be greater than 0 and at most 100 (got -10)" },
        { new PercentageCoupon(100.01m), "Campaign #1 'Percentage discount': percentage must be greater than 0 and at most 100 (got 100.01)" },
        { new PercentageCoupon(0.001m), "Campaign #1 'Percentage discount': percentage must have at most 2 decimal places (got 0.001)" },
        { new PercentageCoupon(33.333m), "Campaign #1 'Percentage discount': percentage must have at most 2 decimal places (got 33.333)" },
        // Just below 0.5%: decimal would round 0.004999...9 up to 0.005 and turn a 1.00 order into 0.99.
        { new PercentageCoupon(0.5m - 1e-28m), "Campaign #1 'Percentage discount': percentage must have at most 2 decimal places (got 0.4999999999999999999999999999)" },
        { new CategoryPercentageOnTop(ItemCategory.Clothing, 0m), "Campaign #1 'Percentage discount by item category': percentage must be greater than 0 and at most 100 (got 0)" },
        { new CategoryPercentageOnTop(ItemCategory.Clothing, 150m), "Campaign #1 'Percentage discount by item category': percentage must be greater than 0 and at most 100 (got 150)" },
        { new CategoryPercentageOnTop(ItemCategory.Clothing, 33.333m), "Campaign #1 'Percentage discount by item category': percentage must have at most 2 decimal places (got 33.333)" },
        { new CategoryPercentageOnTop((ItemCategory)99, 15m), "Campaign #1 'Percentage discount by item category': item category must be one of Clothing, Accessories, Electronics (got 99)" },
        { new PointsOnTop(0), "Campaign #1 'Discount by points': points must be at least 1 (got 0)" },
        { new PointsOnTop(-5), "Campaign #1 'Discount by points': points must be at least 1 (got -5)" },
        { new SeasonalCampaign(0m, 40m), "Campaign #1 'Special campaign': every amount must be greater than 0 (got 0)" },
        { new SeasonalCampaign(300m, 0m), "Campaign #1 'Special campaign': discount amount must be greater than 0 (got 0)" },
        { new SeasonalCampaign(300m, 400m), "Campaign #1 'Special campaign': discount amount must not exceed every amount (got 400 > 300)" },
        { new SeasonalCampaign(1_000_000_000.01m, 40m), "Campaign #1 'Special campaign': every amount is too large (max 1,000,000,000, got 1000000000.01)" },
        { new SeasonalCampaign(300m, 0.005m), "Campaign #1 'Special campaign': discount amount must have at most 2 decimal places (got 0.005)" },
    };

    [Theory]
    [MemberData(nameof(InvalidCampaigns))]
    public void An_invalid_campaign_parameter_is_rejected_naming_the_campaign_and_the_rule(Campaign campaign, string expectedError)
    {
        Assert.Equal(expectedError, SingleError(SampleCarts.TShirtAndHat(), [campaign]));
    }

    public static TheoryData<Campaign> BoundaryCampaigns => new()
    {
        new FixedAmountCoupon(0.01m),
        new FixedAmountCoupon(1_000_000_000m),
        new PercentageCoupon(100m),
        new PercentageCoupon(33.33m),
        new PercentageCoupon(0.01m),
        new CategoryPercentageOnTop(ItemCategory.Clothing, 100m),
        new PointsOnTop(1),
        new SeasonalCampaign(300m, 300m),
        new SeasonalCampaign(0.01m, 0.01m),
        new SeasonalCampaign(1_000_000_000m, 1_000_000_000m),
    };

    [Theory]
    [MemberData(nameof(BoundaryCampaigns))]
    public void A_campaign_parameter_on_the_edge_of_its_range_is_accepted(Campaign campaign)
    {
        DiscountResult result = DiscountCalculator.Calculate(SampleCarts.TShirtAndHat(), [campaign]);

        Assert.Single(result.AppliedDiscounts);
    }

    [Fact]
    public void A_campaign_type_that_reports_an_unknown_category_is_rejected()
    {
        Assert.Equal(
            "Campaign #1 'Broken': category must be one of Coupon, On Top, Seasonal (got 0)",
            SingleError(SampleCarts.TShirtAndHat(), [new UnknownCategoryCampaign()]));
    }

    // ---- How errors are reported ----

    [Fact]
    public void All_problems_are_reported_together_in_input_order()
    {
        Cart cart = new([
            new CartItem("", ItemCategory.Clothing, -10m, 0),
            new CartItem("Hat", (ItemCategory)42, 250m),
        ]);
        Campaign[] campaigns = [new FixedAmountCoupon(-5m), new PercentageCoupon(10m), new PointsOnTop(0)];

        string[] expected =
        [
            "Item #1: name must not be blank",
            "Item #1: unit price must not be negative (got -10)",
            "Item #1: quantity must be at least 1 (got 0)",
            "Item #2 'Hat': category must be one of Clothing, Accessories, Electronics (got 42)",
            "Campaign #1 'Fixed amount': amount must be greater than 0 (got -5)",
            "Campaign #3 'Discount by points': points must be at least 1 (got 0)",
            "Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)",
        ];
        Assert.Equal(expected, Reject(cart, campaigns).Errors);
    }

    [Fact]
    public void A_missing_cart_and_a_missing_campaign_list_are_both_reported()
    {
        string[] expected = ["Cart is required", "Campaign list is required"];

        Assert.Equal(expected, Reject(null!, null!).Errors);
    }

    [Fact]
    public void The_exception_keeps_its_own_copy_of_the_errors()
    {
        List<string> source = ["first problem"];
        DiscountValidationException exception = new(source);

        source[0] = "changed later";
        source.Add("added later");

        string[] expected = ["first problem"];
        Assert.Equal(expected, exception.Errors);
        Assert.Equal("Invalid discount request: first problem", exception.Message);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)exception.Errors).Add("added through a cast"));
    }

    private static DiscountValidationException Reject(Cart cart, IReadOnlyCollection<Campaign> campaigns)
        => Assert.Throws<DiscountValidationException>(() => DiscountCalculator.Calculate(cart, campaigns));

    private static string SingleError(Cart cart, IReadOnlyCollection<Campaign> campaigns)
        => Assert.Single(Reject(cart, campaigns).Errors);

    /// <summary>A campaign type written incorrectly: it reports a category that does not exist.</summary>
    private sealed record UnknownCategoryCampaign : Campaign
    {
        public override CampaignCategory Category => default;
        public override string Name => "Broken";
        public override IEnumerable<string> Validate() => [];
        public override decimal ComputeDiscount(DiscountContext context) => 0m;
        public override string Describe(DiscountContext context) => "";
    }
}
