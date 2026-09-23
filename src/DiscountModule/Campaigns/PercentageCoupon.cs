using DiscountModule.Validation;

using static System.FormattableString;

namespace DiscountModule.Campaigns;

/// <summary>Coupon: subtracts a percentage (0-100) of the order total.</summary>
public sealed record PercentageCoupon(decimal Percentage) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.Coupon;
    public override string Name => "Percentage discount";

    public override IEnumerable<string> Validate() => PercentageRules.Validate(Percentage);

    // Percentage has at most 2 decimal places (validated), so Percentage / 100 has at most 4 and the
    // product with the running total (2 places) is exact; Money.Round in the calculator is the only
    // rounding. Dividing first also keeps the intermediate value below the running total (no overflow).
    public override decimal ComputeDiscount(DiscountContext context)
        => context.RunningTotal * (Percentage / 100m);

    public override string Describe(DiscountContext context)
        => Invariant($"{Percentage:0.##}% off {Money.Format(context.RunningTotal)} THB");
}
