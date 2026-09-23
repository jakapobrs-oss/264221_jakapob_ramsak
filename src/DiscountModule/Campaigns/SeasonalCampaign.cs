using DiscountModule.Validation;

namespace DiscountModule.Campaigns;

/// <summary>Seasonal: for every <see cref="EveryAmount"/> THB of the order total, subtracts <see cref="DiscountAmount"/> THB.</summary>
public sealed record SeasonalCampaign(decimal EveryAmount, decimal DiscountAmount) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.Seasonal;
    public override string Name => "Special campaign";

    public override IEnumerable<string> Validate()
    {
        List<string> problems =
        [
            .. AmountRules.Positive("every amount", EveryAmount),
            .. AmountRules.Positive("discount amount", DiscountAmount),
        ];

        // A5: more than 100% off per step makes no sense. Only checked when both values are
        // positive; otherwise the messages above already explain the problem.
        if (EveryAmount > 0 && DiscountAmount > EveryAmount)
        {
            problems.Add($"discount amount must not exceed every amount (got {DiscountAmount} > {EveryAmount})");
        }

        return problems;
    }

    // A5: floor division, so 830 THB with "40 at every 300" counts 2 full steps = 80 THB.
    public override decimal ComputeDiscount(DiscountContext context)
        => CompletedSteps(context.RunningTotal) * DiscountAmount;

    public override string Describe(DiscountContext context)
        => $"{Money.Format(DiscountAmount)} THB off every {Money.Format(EveryAmount)} THB "
            + $"of {Money.Format(context.RunningTotal)} THB ({CompletedSteps(context.RunningTotal):0} x {Money.Format(DiscountAmount)})";

    private decimal CompletedSteps(decimal runningTotal) => Math.Floor(runningTotal / EveryAmount);
}
