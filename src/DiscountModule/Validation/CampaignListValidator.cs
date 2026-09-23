using DiscountModule.Campaigns;

using static System.FormattableString;

namespace DiscountModule.Validation;

/// <summary>Checks the campaign list rules (design notes, section 5) and returns every problem found, not just the first.</summary>
internal static class CampaignListValidator
{
    private static readonly string SupportedCategories =
        string.Join(", ", Enum.GetValues<CampaignCategory>().Select(category => category.ToDisplayName()));

    public static IEnumerable<string> Validate(IReadOnlyCollection<Campaign>? campaigns)
    {
        if (campaigns is null)
        {
            yield return "Campaign list is required";
            yield break;
        }

        int position = 0;
        foreach (Campaign? campaign in campaigns)
        {
            position++;
            if (campaign is null)
            {
                yield return $"Campaign #{position} is missing";
                continue;
            }

            // The calculator orders by category, so a campaign type that reports an unknown one is rejected.
            if (!Enum.IsDefined(campaign.Category))
            {
                yield return Invariant($"Campaign #{position} '{campaign.Name}': category must be one of {SupportedCategories} (got {(int)campaign.Category})");
            }

            foreach (string problem in campaign.Validate())
            {
                yield return $"Campaign #{position} '{campaign.Name}': {problem}";
            }
        }

        // A7: two campaigns of the same category are a usage error, not something to resolve silently.
        IEnumerable<IGrouping<CampaignCategory, Campaign>> sharedCategories = campaigns
            .OfType<Campaign>()
            .Where(campaign => Enum.IsDefined(campaign.Category))
            .GroupBy(campaign => campaign.Category)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key);

        foreach (IGrouping<CampaignCategory, Campaign> group in sharedCategories)
        {
            string names = string.Join(", ", group.Select(campaign => campaign.Name));
            yield return $"Only one {group.Key.ToDisplayName()} campaign can be applied per order (got: {names})";
        }
    }
}
