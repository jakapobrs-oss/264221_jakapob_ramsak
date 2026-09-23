using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Cli.Output;

/// <summary>Renders a <see cref="DiscountResult"/> either as a human readable breakdown or as JSON.</summary>
public static class ResultPrinter
{
    private const int MinLabelWidth = 56;
    private const int ColumnGap = 2;
    private const int AmountWidth = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void WriteText(Cart cart, DiscountResult result, TextWriter writer)
    {
        // Rows are collected first so the amount column can be aligned to the longest label.
        List<(string Label, string? Amount)> rows = [("Items", null)];
        foreach (var item in cart.Items)
        {
            rows.Add(($"  {item.Quantity} x {item.Name} ({item.Category}) @ {Money(item.UnitPrice)}", Money(item.LineTotal)));
        }

        rows.Add(("Subtotal", Money(result.Subtotal)));
        rows.Add(("", null));
        rows.Add(("Discounts (applied in order: Coupon > On Top > Seasonal)", null));
        if (result.AppliedDiscounts.Count == 0)
        {
            rows.Add(("  (none)", null));
        }

        foreach (var applied in result.AppliedDiscounts)
        {
            rows.Add(($"  [{applied.Campaign.Category.ToDisplayName()}] {applied.Description}", "-" + Money(applied.DiscountAmount)));
        }

        rows.Add(("", null));
        rows.Add(("Total discount", Money(result.TotalDiscount)));
        rows.Add(("Final price", Money(result.FinalPrice)));
        if (result.PointsUsed > 0)
        {
            rows.Add(($"Points used: {result.PointsUsed}", null));
        }

        var labelWidth = Math.Max(MinLabelWidth, rows.Where(row => row.Amount is not null).Max(row => row.Label.Length) + ColumnGap);
        foreach (var (label, amount) in rows)
        {
            writer.WriteLine(amount is null ? label : label.PadRight(labelWidth) + amount.PadLeft(AmountWidth));
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

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
