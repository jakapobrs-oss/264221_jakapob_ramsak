using DiscountModule.Campaigns;
using DiscountModule.Models;

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
    /// <exception cref="Validation.DiscountValidationException">
    /// The cart or a campaign is invalid, or more than one campaign of the same category was given.
    /// </exception>
    public static DiscountResult Calculate(Cart cart, IReadOnlyCollection<Campaign> campaigns)
        => throw new NotImplementedException();
}
