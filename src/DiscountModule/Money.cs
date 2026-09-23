using System.Globalization;

namespace DiscountModule;

/// <summary>
/// Money helpers. Amounts are THB held in <see cref="decimal"/> with satang (2 decimal places) precision,
/// so values such as 2,382.50 stay exact.
/// </summary>
internal static class Money
{
    /// <summary>
    /// Rounds to 2 decimal places, half away from zero (0.005 becomes 0.01, not 0.00). The result always
    /// carries exactly 2 decimal places (550 becomes 550.00, 60.0 becomes 60.00), so every amount in a
    /// result prints the same way however it was computed.
    /// </summary>
    public static decimal Round(decimal amount)
        // decimal remembers trailing zeros; adding 0.00m sets that scale to 2 without changing the value.
        => Math.Round(amount, 2, MidpointRounding.AwayFromZero) + 0.00m;

    /// <summary>
    /// True when the value has at most 2 decimal places, e.g. 19.90 or 20 but not 0.005. For money this
    /// means a whole number of satang; percentages use the same limit.
    /// </summary>
    public static bool HasAtMostTwoDecimalPlaces(decimal value) => value == Math.Round(value, 2);

    /// <summary>Formats an amount for descriptions, e.g. 1050 becomes "1,050.00".</summary>
    public static string Format(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
