using DiscountModule.Campaigns;
using DiscountModule.Cli.Input;
using DiscountModule.Models;

namespace DiscountModule.Cli.Tests;

public class OrderRequestMapperTests
{
    private static CartItemDto Item(string name = "T-Shirt", string category = "Clothing", decimal price = 350m, int? quantity = null)
        => new() { Name = name, Category = category, UnitPrice = price, Quantity = quantity };

    private static OrderRequestDto Request(params CampaignDto[] campaigns)
        => new() { Items = [Item()], Campaigns = [.. campaigns] };

    private static InputFormatException MapFails(OrderRequestDto dto)
        => Assert.Throws<InputFormatException>(() => OrderRequestMapper.Map(dto));

    [Fact]
    public void Maps_items_with_quantity_defaulting_to_one()
    {
        var dto = new OrderRequestDto { Items = [Item(), Item("Hat", "accessories", 250m, quantity: 3)] };

        var request = OrderRequestMapper.Map(dto);

        Assert.Collection(request.Cart.Items,
            item => Assert.Equal(new CartItem("T-Shirt", ItemCategory.Clothing, 350m, 1), item),
            item => Assert.Equal(new CartItem("Hat", ItemCategory.Accessories, 250m, 3), item));
        Assert.Empty(request.Campaigns);
    }

    [Fact]
    public void Maps_every_campaign_type_case_insensitively()
    {
        var dto = Request(
            new CampaignDto { Type = "fixedamount", Amount = 50m },
            new CampaignDto { Type = "PERCENTAGE", Percentage = 10m },
            new CampaignDto { Type = "CategoryPercentage", Category = "clothing", Percentage = 15m },
            new CampaignDto { Type = "Points", Points = 68 },
            new CampaignDto { Type = "Seasonal", EveryAmount = 300m, DiscountAmount = 40m });

        var request = OrderRequestMapper.Map(dto);

        Assert.Equal(
            [
                new FixedAmountCoupon(50m),
                new PercentageCoupon(10m),
                new CategoryPercentageOnTop(ItemCategory.Clothing, 15m),
                new PointsOnTop(68),
                new SeasonalCampaign(300m, 40m),
            ],
            request.Campaigns);
    }

    [Fact]
    public void Missing_campaigns_property_means_no_campaigns()
    {
        var request = OrderRequestMapper.Map(new OrderRequestDto { Items = [Item()], Campaigns = null });

        Assert.Empty(request.Campaigns);
    }

    [Fact]
    public void Passes_business_values_through_for_the_module_to_validate()
    {
        // Negative prices and out-of-range percentages are business rules, not format errors.
        var dto = new OrderRequestDto
        {
            Items = [Item(price: -1m, quantity: 0)],
            Campaigns = [new CampaignDto { Type = "Percentage", Percentage = 250m }],
        };

        var request = OrderRequestMapper.Map(dto);

        Assert.Equal(-1m, request.Cart.Items[0].UnitPrice);
        Assert.Equal(new PercentageCoupon(250m), request.Campaigns[0]);
    }

    [Fact]
    public void Rejects_missing_items_property()
    {
        var error = MapFails(new OrderRequestDto { Items = null, Campaigns = [] });

        Assert.Equal(["\"items\" is required"], error.Errors);
    }

    [Fact]
    public void Rejects_unknown_campaign_type_and_lists_the_supported_ones()
    {
        var error = MapFails(Request(new CampaignDto { Type = "BuyOneGetOne", Amount = 1m }));

        var message = Assert.Single(error.Errors);
        Assert.Contains("unknown type 'BuyOneGetOne'", message);
        Assert.Contains("FixedAmount, Percentage, CategoryPercentage, Points, Seasonal", message);
    }

    [Fact]
    public void Rejects_unknown_item_category_and_lists_the_supported_ones()
    {
        var error = MapFails(new OrderRequestDto { Items = [Item(category: "Shoes")], Campaigns = [] });

        var message = Assert.Single(error.Errors);
        Assert.Contains("unknown category 'Shoes'", message);
        Assert.Contains("Clothing, Accessories, Electronics", message);
    }

    [Fact]
    public void Rejects_numeric_category_strings()
    {
        // Enum.TryParse would happily accept "7"; the mapper must not.
        var error = MapFails(new OrderRequestDto { Items = [Item(category: "7")], Campaigns = [] });

        Assert.Contains("unknown category '7'", Assert.Single(error.Errors));
    }

    [Fact]
    public void Rejects_missing_campaign_parameter()
    {
        var error = MapFails(Request(new CampaignDto { Type = "Seasonal", EveryAmount = 300m }));

        Assert.Equal(["Campaign #1 (Seasonal): \"discountAmount\" is required"], error.Errors);
    }

    [Fact]
    public void Rejects_parameter_that_does_not_belong_to_the_campaign_type()
    {
        var error = MapFails(Request(new CampaignDto { Type = "Points", Points = 10, Amount = 50m }));

        var message = Assert.Single(error.Errors);
        Assert.Contains("unexpected parameter \"amount\"", message);
        Assert.Contains("only takes: points", message);
    }

    [Fact]
    public void Rejects_campaign_without_type()
    {
        var error = MapFails(Request(new CampaignDto { Amount = 50m }));

        Assert.Contains("\"type\" is required", Assert.Single(error.Errors));
    }

    [Fact]
    public void Collects_every_problem_before_failing()
    {
        var dto = new OrderRequestDto
        {
            Items = [new CartItemDto { Category = "Toys" }],
            Campaigns = [new CampaignDto { Type = "Nope" }, new CampaignDto { Type = "Points" }],
        };

        var error = MapFails(dto);

        Assert.Equal(
            [
                "Item #1: \"name\" is required",
                "Item #1: \"unitPrice\" is required",
                "Item #1: unknown category 'Toys' (supported: Clothing, Accessories, Electronics)",
                "Campaign #1: unknown type 'Nope' (supported: FixedAmount, Percentage, CategoryPercentage, Points, Seasonal)",
                "Campaign #2 (Points): \"points\" is required",
            ],
            error.Errors);
    }
}
