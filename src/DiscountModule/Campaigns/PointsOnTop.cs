using static System.FormattableString;

namespace DiscountModule.Campaigns;

/// <summary>
/// On Top: the customer spends points, 1 point = 1 THB, capped at <see cref="MaxShareOfTotal"/>
/// of the current order total (fixed business rule).
/// </summary>
public sealed record PointsOnTop(int Points) : Campaign
{
    /// <summary>Fixed rule from the requirements: the points discount never exceeds 20% of the total.</summary>
    public const decimal MaxShareOfTotal = 0.20m;

    public override CampaignCategory Category => CampaignCategory.OnTop;
    public override string Name => "Discount by points";

    public override IEnumerable<string> Validate()
    {
        if (Points < 1)
        {
            yield return Invariant($"points must be at least 1 (got {Points})");
        }
    }

    public override decimal ComputeDiscount(DiscountContext context)
        => Math.Min(Points, MaxPointsFor(context.RunningTotal));

    public override string Describe(DiscountContext context)
        => Invariant($"{Points} points (cap {MaxPointsFor(context.RunningTotal):0} = ")
            + Invariant($"{MaxShareOfTotal * 100:0}% of {Money.Format(context.RunningTotal)} THB)");

    // A2: points are whole units, so the cap is rounded down to a whole baht (762.50 -> 152).
    private static decimal MaxPointsFor(decimal runningTotal) => Math.Floor(runningTotal * MaxShareOfTotal);
}
