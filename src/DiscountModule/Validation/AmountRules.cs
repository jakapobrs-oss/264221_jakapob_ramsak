using System.Globalization;

namespace DiscountModule.Validation;

/// <summary>
/// Rules shared by every money input (unit price, coupon amount, seasonal amounts), so they are
/// all checked the same way: within range and a whole number of satang.
/// </summary>
internal static class AmountRules
{
    private static readonly string MaxAmountText = InputLimits.MaxAmount.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>For prices, where 0 is allowed (a free item).</summary>
    public static IEnumerable<string> NonNegative(string field, decimal value)
    {
        if (value < 0)
        {
            yield return $"{field} must not be negative (got {value})";
        }

        foreach (string problem in RangeAndPrecision(field, value))
        {
            yield return problem;
        }
    }

    /// <summary>For campaign parameters, where 0 would make the campaign meaningless.</summary>
    public static IEnumerable<string> Positive(string field, decimal value)
    {
        if (value <= 0)
        {
            yield return $"{field} must be greater than 0 (got {value})";
        }

        foreach (string problem in RangeAndPrecision(field, value))
        {
            yield return problem;
        }
    }

    private static IEnumerable<string> RangeAndPrecision(string field, decimal value)
    {
        if (value > InputLimits.MaxAmount)
        {
            yield return $"{field} is too large (max {MaxAmountText}, got {value})";
        }

        if (!Money.HasAtMostTwoDecimalPlaces(value))
        {
            yield return $"{field} must have at most 2 decimal places (got {value})";
        }
    }
}
