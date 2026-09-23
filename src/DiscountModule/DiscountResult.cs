using DiscountModule.Campaigns;

namespace DiscountModule;

/// <summary>Outcome of applying campaigns to a cart, including a step-by-step breakdown.</summary>
public sealed record DiscountResult(
    decimal Subtotal,
    IReadOnlyList<AppliedDiscount> AppliedDiscounts,
    decimal FinalPrice)
{
    /// <summary>Total amount subtracted across all campaigns.</summary>
    public decimal TotalDiscount => Subtotal - FinalPrice;

    /// <summary>Points actually consumed by a <see cref="PointsOnTop"/> campaign (0 when none was applied).</summary>
    public int PointsUsed => (int)(AppliedDiscounts
        .FirstOrDefault(applied => applied.Campaign is PointsOnTop)?.DiscountAmount ?? 0m);
}
