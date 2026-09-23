using DiscountModule.Models;

namespace DiscountModule.Campaigns;

/// <summary>On Top: subtracts a percentage (0-100) of the price of every item in one category.</summary>
public sealed record CategoryPercentageOnTop(ItemCategory ItemCategory, decimal Percentage) : Campaign
{
    public override CampaignCategory Category => CampaignCategory.OnTop;
    public override string Name => "Percentage discount by item category";

    public override IEnumerable<string> Validate() => throw new NotImplementedException();

    public override decimal ComputeDiscount(DiscountContext context) => throw new NotImplementedException();
}
