using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Cli.Output;

/// <summary>Renders a <see cref="DiscountResult"/> either as a human readable breakdown or as JSON.</summary>
public static class ResultPrinter
{
    private const int LabelWidth = 56;
    private const int AmountWidth = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void WriteText(Cart cart, DiscountResult result, TextWriter writer)
    {
        writer.WriteLine("Items");
        foreach (var item in cart.Items)
        {
            WriteRow(writer, $"  {item.Quantity} x {item.Name} ({item.Category}) @ {Money(item.UnitPrice)}", Money(item.LineTotal));
        }

        WriteRow(writer, "Subtotal", Money(result.Subtotal));
        writer.WriteLine();

        writer.WriteLine("Discounts (applied in order: Coupon > On Top > Seasonal)");
        if (result.AppliedDiscounts.Count == 0)
        {
            writer.WriteLine("  (none)");
        }

        foreach (var applied in result.AppliedDiscounts)
        {
            WriteRow(writer, $"  [{Label(applied.Campaign.Category)}] {applied.Description}", "-" + Money(applied.DiscountAmount));
        }

        writer.WriteLine();
        WriteRow(writer, "Total discount", Money(result.TotalDiscount));
        WriteRow(writer, "Final price", Money(result.FinalPrice));
        if (result.PointsUsed > 0)
        {
            writer.WriteLine($"Points used: {result.PointsUsed}");
        }
    }

    public static void WriteJson(DiscountResult result, TextWriter writer)
    {
        var payload = new
        {
            result.Subtotal,
            Discounts = result.AppliedDiscounts.Select(applied => new
            {
                applied.Campaign.Category,
                Campaign = applied.Campaign.Name,
                applied.Description,
                applied.PriceBefore,
                applied.DiscountAmount,
                applied.PriceAfter,
            }),
            result.TotalDiscount,
            result.PointsUsed,
            result.FinalPrice,
        };

        writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static void WriteRow(TextWriter writer, string label, string amount)
        => writer.WriteLine(label.PadRight(LabelWidth) + amount.PadLeft(AmountWidth));

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Label(CampaignCategory category) => category switch
    {
        CampaignCategory.Coupon => "Coupon",
        CampaignCategory.OnTop => "On Top",
        CampaignCategory.Seasonal => "Seasonal",
        _ => category.ToString(),
    };
}
