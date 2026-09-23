using DiscountModule.Models;

namespace DiscountModule.Campaigns;

/// <summary>
/// State visible to a campaign when it computes its discount.
/// <paramref name="RunningTotal"/> is the order total after all previously applied campaigns.
/// </summary>
public sealed record DiscountContext(Cart Cart, decimal RunningTotal);
