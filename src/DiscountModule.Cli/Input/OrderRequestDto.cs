using System.Text.Json.Serialization;

namespace DiscountModule.Cli.Input;

/// <summary>
/// Shape of the JSON input file. Every field is optional at this level so that the mapper can
/// report all missing or unexpected values at once instead of failing on the first one.
/// Unknown properties are rejected to catch typos such as "price" instead of "unitPrice".
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OrderRequestDto
{
    public List<CartItemDto>? Items { get; set; }

    public List<CampaignDto>? Campaigns { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CartItemDto
{
    public string? Name { get; set; }

    /// <summary>Clothing, Accessories or Electronics (case-insensitive).</summary>
    public string? Category { get; set; }

    public decimal? UnitPrice { get; set; }

    /// <summary>Defaults to 1 when omitted.</summary>
    public int? Quantity { get; set; }
}

/// <summary>
/// One campaign entry. <see cref="Type"/> selects the campaign; the remaining properties are the
/// parameters of that campaign type (see <see cref="OrderRequestMapper"/> for which ones apply).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CampaignDto
{
    /// <summary>FixedAmount, Percentage, CategoryPercentage, Points or Seasonal (case-insensitive).</summary>
    public string? Type { get; set; }

    public decimal? Amount { get; set; }

    public decimal? Percentage { get; set; }

    public string? Category { get; set; }

    public int? Points { get; set; }

    public decimal? EveryAmount { get; set; }

    public decimal? DiscountAmount { get; set; }
}
