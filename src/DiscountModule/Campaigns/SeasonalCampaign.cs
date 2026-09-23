namespace DiscountModule.Campaigns;

/// <summary>Seasonal: for every <see cref="EveryAmount"/> THB of the order total, subtracts <see cref="DiscountAmount"/> THB.</summary>
public sealed record SeasonalCampaign(decimal EveryAmount, decimal DiscountAmount) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.Seasonal;
    public override string Name => "Special campaign";

    public override IEnumerable<string> Validate() => throw new NotImplementedException();

    public override decimal ComputeDiscount(DiscountContext context) => throw new NotImplementedException();
}
