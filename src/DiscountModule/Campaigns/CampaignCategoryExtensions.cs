namespace DiscountModule.Campaigns;

/// <summary>Display helpers for <see cref="CampaignCategory"/>.</summary>
public static class CampaignCategoryExtensions
{
    /// <summary>Name used in messages and output, e.g. <see cref="CampaignCategory.OnTop"/> becomes "On Top".</summary>
    public static string ToDisplayName(this CampaignCategory category) => category switch
    {
        CampaignCategory.Coupon => "Coupon",
        CampaignCategory.OnTop => "On Top",
        CampaignCategory.Seasonal => "Seasonal",
        _ => category.ToString(),
    };
}
