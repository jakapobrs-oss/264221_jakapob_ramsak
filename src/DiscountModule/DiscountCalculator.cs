using DiscountModule.Campaigns;
using DiscountModule.Models;
using DiscountModule.Validation;

namespace DiscountModule;

/// <summary>
/// Entry point of the discount module: validates the input, orders the campaigns
/// (Coupon, then On Top, then Seasonal) and applies them one after another.
/// </summary>
public static class DiscountCalculator
{
    /// <summary>
    /// Calculates the final price of <paramref name="cart"/> after applying <paramref name="campaigns"/>.
    /// </summary>
    /// <exception cref="DiscountValidationException">
    /// The cart or a campaign is invalid, or more than one campaign of the same category was given.
    /// </exception>
    public static DiscountResult Calculate(Cart cart, IReadOnlyCollection<Campaign> campaigns)
    {
        // Collect every problem before failing, so the caller can fix them all in one go.
        List<string> errors = [.. CartValidator.Validate(cart), .. CampaignListValidator.Validate(campaigns)];
        if (errors.Count > 0)
        {
            throw new DiscountValidationException(errors);
        }

        // Valid prices are whole satang within InputLimits, so the subtotal is exact and cannot overflow.
        // Money.Round does not change its value here; it only fixes it at 2 decimal places.
        decimal subtotal = Money.Round(cart.Subtotal);
        decimal runningTotal = subtotal;
        List<AppliedDiscount> breakdown = [];

        // Coupon -> On Top -> Seasonal whatever order the caller used. OrderBy builds a new
        // sequence, so the caller's collection keeps its own order.
        IEnumerable<Campaign> applicationOrder = campaigns.OrderBy(campaign => campaign.Category);

        foreach (Campaign campaign in applicationOrder)
        {
            DiscountContext context = new(cart, runningTotal);

            // A step never takes more than what is left and never adds to the price (A6).
            decimal discount = Money.Round(Math.Clamp(campaign.ComputeDiscount(context), 0m, runningTotal));
            decimal priceAfter = runningTotal - discount;

            breakdown.Add(new AppliedDiscount(campaign, runningTotal, discount, priceAfter, campaign.Describe(context)));
            runningTotal = priceAfter;
        }

        return new DiscountResult(subtotal, breakdown.AsReadOnly(), runningTotal);
    }
}
