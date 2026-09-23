using static System.FormattableString;

namespace DiscountModule.Validation;

/// <summary>Rules shared by both percentage campaigns, so they are checked the same way.</summary>
internal static class PercentageRules
{
    public static IEnumerable<string> Validate(decimal percentage)
    {
        if (percentage <= 0 || percentage > 100)
        {
            yield return Invariant($"percentage must be greater than 0 and at most 100 (got {percentage})");
        }

        // With at most 2 decimal places, percentage / 100 has at most 4, so the discount is computed
        // exactly and Money.Round is the only rounding step. More places could make decimal round the
        // intermediate value first (0.4999...% would act as 0.5%).
        if (!Money.HasAtMostTwoDecimalPlaces(percentage))
        {
            yield return Invariant($"percentage must have at most 2 decimal places (got {percentage})");
        }
    }
}
