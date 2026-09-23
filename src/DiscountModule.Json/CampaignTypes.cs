using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Json;

/// <summary>
/// The campaign types accepted in the JSON input and the parameters each one reads.
/// Accepting a new campaign type from JSON (CLI and HTTP API alike) means adding one entry to <see cref="Types"/>;
/// the rule itself lives in the module as a new <see cref="Campaign"/> record.
/// </summary>
internal static class CampaignTypes
{
    private static readonly CampaignType[] Types =
    [
        new("FixedAmount", json => json.Number("amount") is { } amount ? new FixedAmountCoupon(amount) : null),
        new("Percentage", json => json.Number("percentage") is { } percentage ? new PercentageCoupon(percentage) : null),

        // Both parameters are read before matching, so both are reported when both are wrong.
        new("CategoryPercentage", json => (json.OneOf<ItemCategory>("category"), json.Number("percentage")) is ({ } category, { } percentage)
            ? new CategoryPercentageOnTop(category, percentage)
            : null),
        new("Points", json => json.WholeNumber("points") is { } points ? new PointsOnTop(points) : null),
        new("Seasonal", json => (json.Number("everyAmount"), json.Number("discountAmount")) is ({ } everyAmount, { } discountAmount)
            ? new SeasonalCampaign(everyAmount, discountAmount)
            : null),
    ];

    private static readonly string[] Names = [.. Types.Select(type => type.Name)];

    /// <summary>
    /// Reads one campaign object. Its "type" picks the entry, and the entry reads that type's parameters.
    /// Returns null when anything is wrong; <paramref name="json"/> holds the problems.
    /// </summary>
    public static Campaign? Read(JsonObjectReader json)
    {
        string? typeName = json.OneOf("type", Names);
        if (typeName is null)
        {
            // Without a known type there is no way to tell which other properties belong here.
            return null;
        }

        Campaign? campaign = Types.Single(type => type.Name == typeName).Read(json);
        json.RejectUnexpectedProperties();
        return json.HasErrors ? null : campaign;
    }

    private sealed record CampaignType(string Name, Func<JsonObjectReader, Campaign?> Read);
}
