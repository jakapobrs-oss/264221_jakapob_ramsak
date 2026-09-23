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

    /// <summary>
    /// The part of <paramref name="amount"/> that <paramref name="part"/> makes up of <paramref name="whole"/>
    /// (amount x part / whole), rounded once to 2 decimal places, half away from zero. For example, the share of
    /// 100.00 THB that 1,000.00 of 1,100.00 THB make up is 90.91. All three are whole satang from 0 to
    /// <see cref="InputLimits.MaxSubtotal"/>, and <paramref name="whole"/> is above 0.
    /// </summary>
    /// <remarks>
    /// Worked out in satang with 128-bit integers, so the value is exact until its one rounding. In decimal,
    /// amount x part can pass the type's range (10^15 x 10^15 THB), and dividing first would round the
    /// quotient to 28 digits before this rounding, so a value could be rounded twice.
    /// </remarks>
    public static decimal Share(decimal amount, decimal part, decimal whole)
    {
        Int128 numerator = ToSatang(amount) * ToSatang(part);
        Int128 denominator = ToSatang(whole);

        // For values of 0 or more, (2n + d) / 2d in integer division is n / d rounded half away from zero.
        Int128 satang = ((2 * numerator) + denominator) / (2 * denominator);
        return Round((decimal)satang / 100m);
    }

    /// <summary>Formats an amount for descriptions, e.g. 1050 becomes "1,050.00".</summary>
    public static string Format(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static Int128 ToSatang(decimal amount) => (Int128)(amount * 100m);
}
