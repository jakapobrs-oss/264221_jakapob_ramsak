using System.Text.Json;
using System.Text.Json.Serialization;
using DiscountModule.Campaigns;

namespace DiscountModule.Json;

/// <summary>
/// The JSON shape of a priced order. The CLI prints it with --json and the HTTP API returns it, so both
/// front ends describe a result in exactly the same way.
/// </summary>
public sealed record PricedOrder(
    decimal Subtotal,
    IReadOnlyList<PricedStep> Discounts,
    decimal TotalDiscount,
    int PointsUsed,
    decimal FinalPrice)
{
    /// <summary>camelCase names and enum names as text, e.g. <c>"category": "OnTop"</c>.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PricedOrder From(DiscountResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        PricedStep[] steps =
        [
            .. result.AppliedDiscounts.Select(applied => new PricedStep(
                applied.Campaign.Category,
                applied.Campaign.Name,
                applied.Description,
                applied.PriceBefore,
                applied.DiscountAmount,
                applied.PriceAfter)),
        ];

        return new PricedOrder(result.Subtotal, steps, result.TotalDiscount, result.PointsUsed, result.FinalPrice);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>One applied campaign in a <see cref="PricedOrder"/>.</summary>
public sealed record PricedStep(
    CampaignCategory Category,
    string Campaign,
    string Description,
    decimal PriceBefore,
    decimal DiscountAmount,
    decimal PriceAfter);
