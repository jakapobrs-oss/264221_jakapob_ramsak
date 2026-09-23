namespace DiscountModule;

/// <summary>
/// Upper bounds for input values. They are far above any real order, and they keep every amount the
/// calculator handles within the ~28 significant digits of <see cref="decimal"/>: nothing can overflow,
/// and no step is rounded before <c>Money.Round</c>, so the module needs no overflow handling.
/// </summary>
public static class InputLimits
{
    /// <summary>Largest accepted money value (unit price, coupon amount, seasonal amounts), in THB.</summary>
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>Largest accepted quantity for one cart line.</summary>
    public const int MaxQuantity = 1_000_000;

    /// <summary>
    /// Largest accepted cart subtotal, in THB (one line at <see cref="MaxAmount"/> x <see cref="MaxQuantity"/>).
    /// At most 16 digits before the decimal point, so a total with 2 decimals times a rate with 4 decimals
    /// needs at most 22 significant digits and is exact. A larger total could make decimal round the
    /// product first (x.004999 would become x.005 and then round up to the next satang).
    /// </summary>
    public const decimal MaxSubtotal = 1_000_000_000_000_000m;
}
