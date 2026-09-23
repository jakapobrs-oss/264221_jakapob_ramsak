using System.Globalization;
using System.Text;
using DiscountModule.Campaigns;
using DiscountModule.Json;
using DiscountModule.Models;

namespace DiscountModule.Cli.Output;

/// <summary>Renders a <see cref="DiscountResult"/> either as a human readable breakdown or as JSON.</summary>
public static class ResultPrinter
{
    private const int MinLabelWidth = 56;
    private const int ColumnGap = 2;
    private const int AmountWidth = 12;

    public static void WriteText(Cart cart, DiscountResult result, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(writer);

        // Rows are collected first so that the amount column can be aligned to the longest label.
        List<(string Label, string? Amount)> rows = [("Items", null)];
        foreach (CartItem item in cart.Items)
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

        foreach (AppliedDiscount applied in result.AppliedDiscounts)
        {
            rows.Add(($"  [{applied.Campaign.Category.ToDisplayName()}] {applied.Description}", Subtracted(applied.DiscountAmount)));
        }

        rows.Add(("", null));
        rows.Add(("Total discount", Subtracted(result.TotalDiscount)));
        rows.Add(("Final price", Money(result.FinalPrice)));
        if (result.PointsUsed > 0)
        {
            rows.Add(($"Points used: {result.PointsUsed}", null));
        }

        // Item names come from the input: a newline or ESC in a name must not break the layout.
        rows = [.. rows.Select(row => (ConsoleText.Escape(row.Label), row.Amount))];

        int labelWidth = Math.Max(MinLabelWidth, rows.Where(row => row.Amount is not null).Max(row => DisplayWidth(row.Label)) + ColumnGap);
        foreach ((string label, string? amount) in rows)
        {
            writer.WriteLine(amount is null ? label : PadToWidth(label, labelWidth) + amount.PadLeft(AmountWidth));
        }
    }

    public static void WriteJson(DiscountResult result, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(writer);

        // Same shape as the HTTP API returns, so a script can switch between the two.
        writer.WriteLine(PricedOrder.From(result).ToJson());
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>An amount taken off the price, e.g. "-83.00"; nothing taken off prints as "0.00", not "-0.00".</summary>
    private static string Subtracted(decimal amount) => amount == 0 ? Money(amount) : "-" + Money(amount);

    /// <summary>
    /// Number of terminal columns the text takes. Combining marks, such as Thai vowel and tone marks written
    /// above or below a consonant, take no column of their own. Wide East Asian characters are not handled.
    /// </summary>
    private static int DisplayWidth(string text)
        => text.EnumerateRunes().Count(rune => Rune.GetUnicodeCategory(rune)
            is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format));

    private static string PadToWidth(string text, int width)
        => text + new string(' ', Math.Max(0, width - DisplayWidth(text)));
}
