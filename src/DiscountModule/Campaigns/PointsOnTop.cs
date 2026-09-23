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

    public override IEnumerable<string> Validate() => throw new NotImplementedException();

    public override decimal ComputeDiscount(DiscountContext context) => throw new NotImplementedException();
}
