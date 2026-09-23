namespace DiscountModule.Campaigns;

/// <summary>
/// Campaign categories. The numeric order is the order in which campaigns are applied
/// (Coupon, then On Top, then Seasonal). At most one campaign per category may be applied.
/// </summary>
public enum CampaignCategory
{
    Coupon = 1,
    OnTop = 2,
    Seasonal = 3,
}
