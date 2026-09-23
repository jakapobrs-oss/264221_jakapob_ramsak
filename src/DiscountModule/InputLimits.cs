namespace DiscountModule;

/// <summary>
/// Upper bounds for input values. They are far above any real order, and they guarantee that no
/// calculation can overflow <see cref="decimal"/>, so the module needs no overflow handling.
/// </summary>
public static class InputLimits
{
    /// <summary>Largest accepted money value (unit price, coupon amount, seasonal amounts), in THB.</summary>
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>Largest accepted quantity for one cart line.</summary>
    public const int MaxQuantity = 1_000_000;
}
